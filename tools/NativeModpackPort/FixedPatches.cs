using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

static class FixedPatches
{
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source)
    {foreach(var t in source){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
    static MethodDefinition Clone(MethodDefinition method,string name)
    {
        var copy=new MethodDefinition(name,MethodAttributes.Public|MethodAttributes.HideBySig|(method.IsStatic?MethodAttributes.Static:0),method.ReturnType);
        foreach(var p in method.Parameters)copy.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,p.ParameterType));
        method.DeclaringType.Methods.Add(copy);
        var body=new MethodBody(copy){InitLocals=method.Body.InitLocals,MaxStackSize=method.Body.MaxStackSize};copy.Body=body;
        var variables=new Dictionary<VariableDefinition,VariableDefinition>();
        foreach(var v in method.Body.Variables){var next=new VariableDefinition(v.VariableType);body.Variables.Add(next);variables[v]=next;}
        var instructions=new Dictionary<Instruction,Instruction>();
        foreach(var old in method.Body.Instructions){var next=Instruction.Create(OpCodes.Nop);next.OpCode=old.OpCode;body.Instructions.Add(next);instructions[old]=next;}
        foreach(var old in method.Body.Instructions){instructions[old].Operand=old.Operand switch {
            Instruction i=>instructions[i],Instruction[] all=>all.Select(i=>instructions[i]).ToArray(),VariableDefinition v=>variables[v],
            ParameterDefinition p=>p.Index<0?body.ThisParameter:copy.Parameters[p.Index],_=>old.Operand};}
        Instruction? Map(Instruction? i)=>i==null?null:instructions[i];
        foreach(var h in method.Body.ExceptionHandlers)body.ExceptionHandlers.Add(new ExceptionHandler(h.HandlerType){CatchType=h.CatchType,TryStart=Map(h.TryStart),TryEnd=Map(h.TryEnd),HandlerStart=Map(h.HandlerStart),HandlerEnd=Map(h.HandlerEnd),FilterStart=Map(h.FilterStart)});
        var opcodes=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToArray();
        foreach(var i in body.Instructions)if(i.OpCode.OperandType==OperandType.ShortInlineBrTarget)i.OpCode=opcodes.Single(o=>o.Name==i.OpCode.Name[..^2]);
        return copy;
    }
    public static void Build(string root)
    {
        var stage=Path.Combine(root,"native-stage");
        var reportPath=Path.Combine(root,"native-fixed-patches.json");
        if(File.Exists(reportPath))throw new IOException("Fixed variants already generated");
        foreach(var path in Directory.GetFiles(Path.Combine(root,"inputs"),"*.dll",SearchOption.AllDirectories)){
            using var input=AssemblyDefinition.ReadAssembly(path);File.Copy(path,Path.Combine(stage,input.Name.Name+".dll"),true);
        }
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(stage);
        var loaded=new Dictionary<string,AssemblyDefinition>();
        AssemblyDefinition Load(string name){if(!loaded.TryGetValue(name,out var a))loaded[name]=a=AssemblyDefinition.ReadAssembly(Path.Combine(stage,name+".dll"),new ReaderParameters{AssemblyResolver=resolver,InMemory=true});return a;}
        var game=Load("Assembly-CSharp");
        var flag=Load("NativeModRuntime").MainModule.GetType("NativeModpack.PatchFlags").Methods.Single(m=>m.Name=="Enabled");
        var originalNames=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"native-static-hooks.json"))).RootElement.EnumerateArray().ToDictionary(x=>x.GetProperty("Target").GetString()!,x=>x.GetProperty("Original").GetString()!);
        var leaves=new Dictionary<MethodDefinition,List<MethodDefinition>>();
        var reports=new List<object>();int serial=0;
        TypeDefinition Type(string name)=>Types(game.MainModule.Types).Single(t=>t.FullName==name);
        MethodDefinition Method(string type,string name)=>Type(type).Methods.Single(m=>m.Name==name);
        List<MethodDefinition> Iterators(string type,string prefix)=>Type(type).NestedTypes.Where(t=>t.Name.Contains("<"+prefix+">")).SelectMany(t=>t.Methods.Where(m=>m.Name=="MoveNext")).ToList();
        void Apply(MethodDefinition target,string key,Func<MethodDefinition,int> edit,bool required=true)
        {
            if(!leaves.TryGetValue(target,out var current)){
                var actual=originalNames.TryGetValue(target.FullName,out var name)?target.DeclaringType.Methods.Single(m=>m.Name==name):target;
                leaves[target]=current=new List<MethodDefinition>{actual};
            }
            var next=new List<MethodDefinition>();
            foreach(var leaf in current){
                var normal=Clone(leaf,"NativeBase_"+(++serial));
                var variant=Clone(leaf,"NativeVariant_"+serial);
                int edits=edit(variant);
                if(required&&edits==0)throw new InvalidOperationException("No matching native edit: "+key+" -> "+target.FullName);
                leaf.Body=new MethodBody(leaf){MaxStackSize=32};var il=leaf.Body.GetILProcessor();
                var fallback=Instruction.Create(OpCodes.Nop);
                il.Emit(OpCodes.Ldstr,key);il.Emit(OpCodes.Call,leaf.Module.ImportReference(flag));il.Emit(OpCodes.Brfalse,fallback);
                void Invoke(MethodDefinition m){if(!leaf.IsStatic)il.Emit(OpCodes.Ldarg_0);foreach(var p in leaf.Parameters)il.Emit(OpCodes.Ldarg,p);il.Emit(OpCodes.Call,m);il.Emit(OpCodes.Ret);}
                Invoke(variant);il.Append(fallback);Invoke(normal);
                next.Add(normal);next.Add(variant);
                reports.Add(new{Feature=key,Target=target.FullName,Normal=normal.Name,Variant=variant.Name,Edits=edits});
            }
            leaves[target]=next;
        }
        static bool Call(Instruction i,string owner,string name)=>i.Operand is MethodReference m&&m.DeclaringType.Name==owner&&m.Name==name;
        static bool Int(Instruction i,int value)=>i.OpCode==OpCodes.Ldc_I4||i.OpCode==OpCodes.Ldc_I4_S?Convert.ToInt32(i.Operand)==value:(int)i.OpCode.Code-(int)Code.Ldc_I4_0==value;
        static bool Local(Instruction i,int index)=>i.Operand is VariableDefinition v&&(i.OpCode==OpCodes.Ldloc||i.OpCode==OpCodes.Ldloc_S)?v.Index==index:(int)i.OpCode.Code-(int)Code.Ldloc_0==index;
        static void Nop(Instruction i){i.OpCode=OpCodes.Nop;i.Operand=null;}
        static int Constants(MethodDefinition m,Func<float,float> transform){int n=0;foreach(var i in m.Body.Instructions)if(i.OpCode==OpCodes.Ldc_R4){float old=(float)i.Operand,value=transform(old);if(value!=old){i.Operand=value;n++;}}return n;}
        var menus=new[]{("UIManager","HideSaveProfileMenu"),("UIManager","HideCurrentMenu"),("UIManager","HideMenu"),("UIManager","ShowMenu"),("UIManager","GoToProfileMenu"),("GameManager","PauseGameToggle"),("GameManager","RunContinueGame"),("UnityEngine.UI.SaveSlotButton","AnimateToSlotState")};
        foreach(var (type,name) in menus){var targets=Iterators(type,name);if(targets.Count==0)throw new MissingMethodException(type,name);foreach(var target in targets)Apply(target,"QoL.FastMenus",m=>Constants(m,f=>name=="RunContinueGame"?(Math.Abs(f-2.6f)<.00001f?.05f:f):name=="PauseGameToggle"&&f==1f?f:0f),false);}
        foreach(var target in Iterators("HeroController","EnterScene"))Apply(target,"QoL.FasterLoads",m=>{
            int n=0;var code=m.Body.Instructions;for(int i=0;i+1<code.Count;i++)if(code[i].OpCode==OpCodes.Ldc_R4&&new[]{.4f,.165f}.Contains((float)code[i].Operand)&&code[i+1].OpCode==OpCodes.Newobj&&code[i+1].Operand is MethodReference c&&c.DeclaringType.Name=="WaitForSeconds"){
                code[i].OpCode=OpCodes.Ldnull;code[i].Operand=null;Nop(code[i+1]);n++;}return n;});
        Apply(Method("HeroController","HeroDash"),"QoL.OldDashmaster",m=>{
            int n=0;var code=m.Body.Instructions;for(int i=0;i+3<code.Count;i++)if(code[i+1].Operand is FieldReference f&&f.DeclaringType.Name=="HeroActions"&&(f.Name=="left"||f.Name=="right")&&Call(code[i+2],"OneAxisInputControl","get_IsPressed")&&(code[i+3].OpCode==OpCodes.Brtrue||code[i+3].OpCode==OpCodes.Brtrue_S)){
                for(int j=0;j<4;j++)Nop(code[i+j]);n++;}return n;});
        foreach(var owner in new[]{"InputHandler","Modding.ModHooks"})Apply(Method(owner,owner=="InputHandler"?"OnGUI":"OnCursor"),"QoL.LockMenuCursor",m=>{
            int n=0;var code=m.Body.Instructions;for(int i=0;i+1<code.Count;i++)if(Int(code[i],0)&&Call(code[i+1],"Cursor","set_lockState")){code[i].OpCode=OpCodes.Ldc_I4_2;code[i].Operand=null;n++;}return n;});
        var debug=Load("DebugMod");var scale=debug.MainModule.GetType("DebugMod.DebugMod").Fields.Single(f=>f.Name=="CurrentTimeScale");
        var freeze=Type("GameManager").Methods.Where(m=>m.Name.StartsWith("FreezeMoment")&&m.ReturnType.FullName=="System.Collections.IEnumerator").ToList();
        foreach(var f in freeze){foreach(var target in Iterators("GameManager",f.Name))Apply(target,"DebugMod.TimeScale",m=>{
            var code=m.Body.Instructions;int index=Enumerable.Range(0,code.Count).FirstOrDefault(i=>Call(code[i],"Time","get_unscaledDeltaTime"),-1);
            if(index<0)return 0;var il=m.Body.GetILProcessor();var load=il.Create(OpCodes.Ldsfld,m.Module.ImportReference(scale));il.InsertAfter(code[index],load);il.InsertAfter(load,il.Create(OpCodes.Mul));return 1;});}
        Apply(Method("HeroAnimationController","PlayIdle"),"GodSeekerPlus.NoLowHealthEffect",m=>{
            var code=m.Body.Instructions;int at=Enumerable.Range(0,code.Count).FirstOrDefault(i=>code[i].OpCode==OpCodes.Ldstr&&(string)code[i].Operand=="Idle Hurt",-1);
            if(at<0)return 0;int last=Enumerable.Range(at,code.Count-at).First(i=>code[i].OpCode==OpCodes.Ret);for(int i=0;i<=last;i++)Nop(code[i]);return last+1;});
        Apply(Method("BossSceneController","get_BossLevel"),"GodSeekerPlus.P5Health",m=>{m.Body=new MethodBody(m);m.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));return 1;});
        var gsp=Load("GodSeekerPlus");var begin=gsp.MainModule.GetType("GodSeekerPlus.Modules.QoL.DoorDefaultBegin").Methods.Single(m=>m.Name=="SelectBegin");begin.IsPublic=true;
        foreach(var target in Iterators("BossDoorChallengeUI","ShowSequence"))Apply(target,"GodSeekerPlus.DoorDefaultBegin",m=>{
            var code=m.Body.Instructions;int at=Enumerable.Range(0,code.Count-1).FirstOrDefault(i=>Local(code[i],1)&&code[i+1].Operand is FieldReference f&&f.Name=="buttons"&&f.DeclaringType.Name=="BossDoorChallengeUI",-1);
            if(at<0)return 0;int end=code.Count-1;while(end>=at&&code[end].OpCode!=OpCodes.Ret)end--;if(end<=at)throw new InvalidOperationException("Door coroutine tail changed");
            var il=m.Body.GetILProcessor();for(int i=at;i<end;i++)Nop(code[i]);il.InsertBefore(code[end],il.Create(OpCodes.Ldloc_1));il.InsertBefore(code[end],il.Create(OpCodes.Call,m.Module.ImportReference(begin)));il.InsertBefore(code[end],il.Create(OpCodes.Ldc_I4_0));return end-at;});
        var qol=Load("QoL");
        var storage=qol.MainModule.GetType("QoL.Modules.SpeedBroke").Fields.Single(f=>f.Name=="Storage");
        foreach(var target in Iterators("HeroController","DieFromHazard"))Apply(target,"QoL.SpeedBroke",m=>{
            int n=0;var code=m.Body.Instructions;var il=m.Body.GetILProcessor();
            for(int i=0;i+2<code.Count;i++)if(Local(code[i],1)&&code[i+1].OpCode==OpCodes.Ldnull&&Call(code[i+2],"HeroController","SetHeroParent")){
                var after=code[i+3];var read=il.Create(OpCodes.Ldsfld,m.Module.ImportReference(storage));var skip=il.Create(OpCodes.Brtrue,after);
                il.InsertBefore(code[i],read);il.InsertAfter(read,skip);n++;i+=4;
            }return n;});
        var spore=qol.MainModule.GetType("QoL.Modules.PatchedBosses").Fields.Single(f=>f.Name=="SporeShroomHollowKnight");
        Apply(Method("ExtraDamageable","RecieveExtraDamage"),"QoL.PatchedBosses",m=>{
            int n=0;var code=m.Body.Instructions;var il=m.Body.GetILProcessor();
            for(int i=0;i<code.Count;i++)if(code[i].OpCode==OpCodes.Ldfld&&code[i].Operand is FieldReference field&&field.DeclaringType.Name=="ExtraDamageable"&&field.Name=="damagedThisFrame"){
                var anchor=code[i];var done=il.Create(OpCodes.Nop);var list=new[]{il.Create(OpCodes.Dup),il.Create(OpCodes.Brfalse,done),il.Create(OpCodes.Ldsfld,m.Module.ImportReference(spore)),il.Create(OpCodes.Ldc_I4_0),il.Create(OpCodes.Ceq),il.Create(OpCodes.And),done};
                foreach(var instruction in list){il.InsertAfter(anchor,instruction);anchor=instruction;}i+=list.Length;n++;
            }return n;});
        Apply(Method("DesktopPlatform","WriteSaveSlot"),"QoL.UnencryptedSaves",m=>{
            var code=m.Body.Instructions;int at=Enumerable.Range(0,code.Count).FirstOrDefault(i=>code[i].OpCode==OpCodes.Ldstr&&System.Text.RegularExpressions.Regex.IsMatch((string)code[i].Operand,@"^_1\.[0-9.]+\.dat$"),-1);
            if(at<0){if(code.Count(i=>Call(i,"File","WriteAllBytes"))<=1)return 0;throw new InvalidOperationException("Unknown versioned save backup format");}
            int end=Enumerable.Range(at,code.Count-at).First(i=>Call(code[i],"File","WriteAllBytes"));
            var handler=m.Body.ExceptionHandlers.Single(h=>h.TryStart!=null&&h.TryEnd!=null&&code.IndexOf(h.TryStart)<=at&&code.IndexOf(h.TryEnd)>at);
            int start=code.IndexOf(handler.TryStart);for(int i=start;i<=end;i++)Nop(code[i]);return end-start+1;
        },false);
        var osmi=Load("Osmi");
        var osmiType=osmi.MainModule.GetType("Osmi.OsmiHooks");
        var onPause=osmiType.Methods.Single(m=>m.Name=="OnGamePause");onPause.IsPublic=true;
        var onUnpause=osmiType.Methods.Single(m=>m.Name=="OnGameUnpause");onUnpause.IsPublic=true;
        foreach(var target in Iterators("GameManager","PauseGameToggle"))Apply(target,"Osmi.PauseEvents",m=>{
            int n=0;var code=m.Body.Instructions;var il=m.Body.GetILProcessor();
            for(int i=0;i+1<code.Count;i++)if(code[i].OpCode==OpCodes.Ldc_R4&&Call(code[i+1],"GameManager","SetTimeScale")){
                float value=(float)code[i].Operand;if(value!=0&&value!=1)continue;
                il.InsertBefore(code[i],il.Create(OpCodes.Call,m.Module.ImportReference(value==0?onPause:onUnpause)));i++;n++;
            }
            // Unity 6 uses SetPausedState(bool) instead of SetTimeScale(float).
            if(n==0)for(int i=0;i+1<code.Count;i++)if(Call(code[i+1],"GameManager","SetPausedState")&&(Int(code[i],0)||Int(code[i],1))){
                il.InsertBefore(code[i],il.Create(OpCodes.Call,m.Module.ImportReference(Int(code[i],1)?onPause:onUnpause)));i++;n++;
            }
            return n;});
        var onHit=osmiType.Methods.Single(m=>m.Name=="OnHitEnemy");onHit.IsPublic=true;
        Apply(Method("HealthManager","Hit"),"Osmi.EnemyHitEvents",m=>{
            var code=m.Body.Instructions;var il=m.Body.GetILProcessor();
            int at=Enumerable.Range(0,code.Count).FirstOrDefault(i=>Call(code[i],"HealthManager","IsBlockingByDirection"),-1);
            if(at<0)return 0;var self=il.Create(OpCodes.Ldarg_0);var hit=il.Create(OpCodes.Ldarg_1);var invoke=il.Create(OpCodes.Call,m.Module.ImportReference(onHit));
            il.InsertAfter(code[at],self);il.InsertAfter(self,hit);il.InsertAfter(hit,invoke);return 1;
        });
        foreach(var assembly in loaded.Values){string temp=Path.Combine(stage,assembly.Name.Name+".dll.fixed");assembly.Write(temp);assembly.Dispose();File.Move(temp,Path.Combine(stage,assembly.Name.Name+".dll"),true);}
        File.WriteAllText(reportPath,JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Generated {reports.Count} fixed native method variants");
    }
}
