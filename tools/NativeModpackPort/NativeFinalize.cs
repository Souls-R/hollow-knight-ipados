using Mono.Cecil;
using Mono.Cecil.Cil;

static class NativeFinalize
{
    public static void Build(string root)
    {
        string stage=Path.Combine(root,"native-stage");
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(stage);
        string path=Path.Combine(stage,"Assembly-CSharp.dll");
        using var assembly=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
        using var loader=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"NativeModLoader.dll"));
        using var runtime=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"NativeModRuntime.dll"));
        var module=assembly.MainModule;
        using var localization=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"TeamCherry.Localization.dll"),new ReaderParameters{AssemblyResolver=resolver});
        var language=localization.MainModule.GetType("TeamCherry.Localization.Language");
        var languageField=language.Fields.Single(f=>f.Name=="LanguageGet");
        var languageDelegate=language.NestedTypes.Single(t=>t.FullName==languageField.FieldType.FullName);
        var languageCallback=module.GetType("Modding.ModHooks").Methods.Single(m=>m.Name=="LanguageGet");
        var invoke=languageDelegate.Methods.Single(m=>m.Name=="Invoke");
        if(invoke.ReturnType.FullName!=languageCallback.ReturnType.FullName||!invoke.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(languageCallback.Parameters.Select(p=>p.ParameterType.FullName)))
            throw new InvalidOperationException("Language callback ABI changed");
        var startManager=module.GetType("StartManager");
        var installer=startManager.Methods.FirstOrDefault(m=>m.Name=="NativeInstallLanguageHook");
        if(installer==null){installer=new MethodDefinition("NativeInstallLanguageHook",MethodAttributes.Public|MethodAttributes.Static,module.TypeSystem.Void);startManager.Methods.Add(installer);}
        installer.Body=new MethodBody(installer);var languageIl=installer.Body.GetILProcessor();
        languageIl.Emit(OpCodes.Ldnull);languageIl.Emit(OpCodes.Ldftn,languageCallback);
        languageIl.Emit(OpCodes.Newobj,module.ImportReference(languageDelegate.Methods.Single(m=>m.IsConstructor)));
        languageIl.Emit(OpCodes.Stsfld,module.ImportReference(languageField));languageIl.Emit(OpCodes.Ret);
        var awake=startManager.Methods.Single(m=>m.Name=="Awake");
        var languageSet=awake.Body.Instructions.FirstOrDefault(i=>i.Operand is GenericInstanceMethod call&&call.DeclaringType.FullName=="Modding.ReflectionHelper"&&call.Name=="SetField");
        if(languageSet!=null){
            int end=awake.Body.Instructions.IndexOf(languageSet);
            if(!awake.Body.Instructions.Take(end).Any(i=>i.OpCode==OpCodes.Ldstr&&Equals(i.Operand,"LanguageGet")))throw new InvalidOperationException("Unexpected startup setter prefix");
            for(int index=0;index<=end;index++){awake.Body.Instructions[index].OpCode=OpCodes.Nop;awake.Body.Instructions[index].Operand=null;}
            awake.Body.Instructions[0].OpCode=OpCodes.Call;awake.Body.Instructions[0].Operand=installer;
        }
        // The Unity 6 journal recorder is static. Preserve the instance when
        // reached through its instance wrapper, and pass the actual name arg.
        var deaths=module.GetType("EnemyDeathEffects");
        var recorder=deaths.Methods.Single(m=>m.Name=="RecordKillForJournal"&&m.IsStatic&&m.Parameters.Count==1);
        var owner=deaths.Fields.FirstOrDefault(f=>f.Name=="NativeJournalOwner");
        if(owner==null){
            owner=new FieldDefinition("NativeJournalOwner",FieldAttributes.Private|FieldAttributes.Static,deaths);deaths.Fields.Add(owner);
            var attribute=new TypeReference("System","ThreadStaticAttribute",module,module.TypeSystem.CoreLibrary);
            owner.CustomAttributes.Add(new CustomAttribute(new MethodReference(".ctor",module.TypeSystem.Void,attribute){HasThis=true}));
        }
        var journal=recorder.Body.Instructions;
        foreach(var read in journal.Where(i=>i.OpCode==OpCodes.Ldfld&&i.Operand is FieldReference f&&f.DeclaringType.FullName==deaths.FullName&&f.Name=="playerDataName").ToArray()){
            int index=journal.IndexOf(read);
            if(index>=2&&journal[index-1].OpCode==OpCodes.Ldarg_0&&journal[index-2].OpCode==OpCodes.Ldarg_0){
                journal[index-2].OpCode=OpCodes.Ldsfld;journal[index-2].Operand=owner;read.OpCode=OpCodes.Nop;read.Operand=null;
            }
        }
        var instanceRecorder=deaths.Methods.Single(m=>m.Name=="RecordKillForJournal"&&!m.IsStatic&&m.Parameters.Count==0);
        instanceRecorder.Body=new MethodBody(instanceRecorder){InitLocals=true};
        var savedOwner=new VariableDefinition(deaths);instanceRecorder.Body.Variables.Add(savedOwner);
        var writer=instanceRecorder.Body.GetILProcessor();
        writer.Emit(OpCodes.Ldsfld,owner);writer.Emit(OpCodes.Stloc,savedOwner);writer.Emit(OpCodes.Ldarg_0);writer.Emit(OpCodes.Stsfld,owner);
        var start=Instruction.Create(OpCodes.Ldarg_0);writer.Append(start);
        writer.Emit(OpCodes.Ldfld,deaths.Fields.Single(f=>f.Name=="playerDataName"));writer.Emit(OpCodes.Call,recorder);
        var finish=Instruction.Create(OpCodes.Ret);writer.Emit(OpCodes.Leave,finish);
        var cleanup=Instruction.Create(OpCodes.Ldloc,savedOwner);writer.Append(cleanup);writer.Emit(OpCodes.Stsfld,owner);writer.Emit(OpCodes.Endfinally);writer.Append(finish);
        instanceRecorder.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally){TryStart=start,TryEnd=cleanup,HandlerStart=cleanup,HandlerEnd=finish});
        foreach(var apiMethod in module.GetType("Modding.Mod").Methods.Where(m=>m.HasBody))
            foreach(var instruction in apiMethod.Body.Instructions)
                if(instruction.Operand is MethodReference call&&call.DeclaringType.FullName=="System.Reflection.Assembly"&&call.Name=="get_Location"){
                    instruction.OpCode=OpCodes.Call;
                    instruction.Operand=module.ImportReference(runtime.MainModule.GetType("NativeModpack.Paths").Methods.Single(m=>m.Name=="AssemblyLocation"));
                }
        foreach(var m in module.GetType("ExtraDamageable").Methods.Where(m=>m.HasBody)){
            var code=m.Body.Instructions;
            foreach(var i in code.Where(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.Name=="damagedThisFrame").ToArray()){
                int index=code.IndexOf(i)+1;
                if(code[index].OpCode==OpCodes.Dup&&code[index+2].Operand is FieldReference f&&f.Name=="SporeShroomHollowKnight")
                    foreach(var operation in code.Skip(index).Take(7)){operation.OpCode=OpCodes.Nop;operation.Operand=null;}
            }
        }
        var iterator=module.GetType("Modding.ModLoader").NestedTypes.Single(t=>t.Name.StartsWith("<LoadModsInit>"));
        var dispose=iterator.Methods.Single(m=>m.Name=="System.IDisposable.Dispose");
        foreach(var i in dispose.Body.Instructions.Where(i=>i.Operand is FieldReference f&&f.Name=="<>8__1").ToArray()){
            int index=dispose.Body.Instructions.IndexOf(i);
            if(i.OpCode!=OpCodes.Stfld||dispose.Body.Instructions[index-1].OpCode!=OpCodes.Ldnull||dispose.Body.Instructions[index-2].OpCode!=OpCodes.Ldarg_0)throw new InvalidOperationException("Unexpected closure cleanup");
            foreach(var operation in dispose.Body.Instructions.Skip(index-2).Take(3)){operation.OpCode=OpCodes.Nop;operation.Operand=null;}
        }
        var method=module.GetType("Modding.ReflectionHelper").Methods.Single(m=>m.Name=="CacheFields");
        var getType=module.GetMemberReferences().OfType<MethodReference>().First(m=>m.DeclaringType.FullName=="System.Type"&&m.Name=="GetTypeFromHandle");
        method.Body=new MethodBody(method);var il=method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldtoken,method.GenericParameters[0]);il.Emit(OpCodes.Call,getType);
        il.Emit(OpCodes.Call,module.ImportReference(loader.MainModule.GetType("NativeModpack.Loader").Methods.Single(m=>m.Name=="WarmType")));il.Emit(OpCodes.Ret);
        var backport=module.AssemblyReferences.Single(r=>r.Name=="MonoMod.Backports");backport.Version=new Version(0,0,0,0);backport.PublicKeyToken=Array.Empty<byte>();
        assembly.Write(path+".finalize");assembly.Dispose();File.Move(path+".finalize",path,true);
        foreach(var file in Directory.GetFiles(stage,"*.dll")){
            using var current=AssemblyDefinition.ReadAssembly(file,new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
            var invalid=current.MainModule.GetMemberReferences().OfType<MethodReference>().Where(m=>m.Name==".ctor"&&!m.HasThis).ToArray();
            if(invalid.Length==0)continue;
            foreach(var constructor in invalid)constructor.HasThis=true;
            current.Write(file+".ctors");current.Dispose();File.Move(file+".ctors",file,true);
            Console.WriteLine(Path.GetFileName(file)+": repaired "+invalid.Length+" constructor instance flags");
        }
        Console.WriteLine("Native reflection cache now warms metadata without runtime generic code generation");
    }
}
