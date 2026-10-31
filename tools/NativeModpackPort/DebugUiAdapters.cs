using Mono.Cecil;
using Mono.Cecil.Cil;

// Keep the original commands and UI; adapt input and presentation at AOT build time.
static class DebugUiAdapters
{
    public static void Build(string root)
    {
        string stage=Path.Combine(root,"native-stage");
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(stage);
        var options=new ReaderParameters{AssemblyResolver=resolver,InMemory=true};
        using var loader=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"NativeModLoader.dll"),options);
        using var runtime=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"NativeModRuntime.dll"),options);
        var presentation=loader.MainModule.GetType("NativeModpack.DebugPresentation");
        var policy=runtime.MainModule.GetType("NativeModpack.DebugPolicy");
        foreach(string name in new[]{"DebugMod","Assembly-CSharp","QoL"})
        {
            string path=Path.Combine(stage,name+".dll");using var assembly=AssemblyDefinition.ReadAssembly(path,options);var module=assembly.MainModule;
            MethodReference Helper(string n)=>module.ImportReference(presentation.Methods.Single(m=>m.Name==n));
            MethodReference Policy(string n)=>module.ImportReference(policy.Methods.Single(m=>m.Name==n));
            bool Has(MethodDefinition m,string n)=>m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName==presentation.FullName&&r.Name==n);
            void Prefix(MethodDefinition m,params Instruction[] instructions){var il=m.Body.GetILProcessor();var first=m.Body.Instructions[0];foreach(var i in instructions)il.InsertBefore(first,i);}
            foreach(var type in module.Types)
            foreach(var method in type.Methods.Where(m=>m.HasBody))
            {
                var code=method.Body.Instructions;var il=method.Body.GetILProcessor();
                if(name=="Assembly-CSharp")foreach(var i in code.Where(i=>i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference r&&r.DeclaringType.FullName=="GameVersionData"&&r.Parameters.Count==0))
                    {i.OpCode=OpCodes.Call;i.Operand=Helper("CreateVersionData");}
                if(name=="DebugMod")foreach(var i in code.Where(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="NativeModpack.LegacyInput"&&r.Name=="GetKeyDown"))
                    i.Operand=module.ImportReference(runtime.MainModule.GetType("NativeModpack.LegacyInput").Methods.Single(m=>m.Name=="DebugGetKeyDown"));
                if(type.FullName=="DebugMod.GUIController"&&method.Name=="BuildMenus"&&!Has(method,"ConfigureFonts"))
                {var load=code.First(i=>i.Operand is MethodReference r&&r.Name=="LoadResources");var arg=Instruction.Create(OpCodes.Ldarg_0);il.InsertAfter(load,arg);il.InsertAfter(arg,Instruction.Create(OpCodes.Call,Helper("ConfigureFonts")));}
                if(type.FullName=="DebugMod.GUIController"&&method.Name=="Update"&&!Has(method,"BeforeUpdate"))
                {
                    Prefix(method,Instruction.Create(OpCodes.Ldarg_0),Instruction.Create(OpCodes.Call,Helper("BeforeUpdate")),Instruction.Create(OpCodes.Brtrue,code[0]),Instruction.Create(OpCodes.Ret));
                    var enumerator=code.First(i=>i.Operand is MethodReference r&&r.Name=="GetEnumerator"&&r.DeclaringType.FullName.StartsWith("System.Collections.Generic.Dictionary`2<System.String,System.Int32>"));
                    il.InsertBefore(enumerator,Instruction.Create(OpCodes.Call,Policy("SnapshotBindings")));
                    foreach(var text in code.Where(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="Toggle All UI").ToArray())
                    {var next=text.Next;if(next?.Operand is MethodReference r&&r.Name=="get_Item"){next.OpCode=OpCodes.Call;next.Operand=Policy("BindingOrNone");}}
                }
                if(type.FullName=="DebugMod.DebugMod"&&method.Name=="get_GM")
                    foreach(var i in code.Where(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="GameManager"&&r.Name=="get_instance"))
                    {var old=(MethodReference)i.Operand;i.Operand=new MethodReference("get_UnsafeInstance",old.ReturnType,old.DeclaringType){HasThis=false};}
                if(type.FullName=="DebugMod.KeyBindPanel")
                {
                    if(method.Name=="UpdateHelpText"&&!Has(method,"PaginateHelp"))
                    {Prefix(method,Instruction.Create(OpCodes.Call,Helper("PaginateHelp")));foreach(var call in code.Where(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="DebugMod.Canvas.CanvasText"&&r.Name=="UpdateText").ToArray())il.InsertBefore(call,Instruction.Create(OpCodes.Call,Helper("DecorateHelp")));}
                    if(method.Name=="ChangeBind"&&!Has(method,"CanCaptureBinding"))Prefix(method,Instruction.Create(OpCodes.Call,Helper("CanCaptureBinding")),Instruction.Create(OpCodes.Brtrue,code[0]),Instruction.Create(OpCodes.Ret));
                    if(method.Name=="RunBind"){method.Body=new MethodBody(method);il=method.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,Helper("RunHelpCommand"));il.Emit(OpCodes.Ret);}
                }
                if(type.Namespace=="DebugMod.Canvas")
                {
                    foreach(var i in code.Where(i=>i.Operand is MethodReference r&&r.Name=="DontDestroyOnLoad")){i.OpCode=OpCodes.Pop;i.Operand=null;}
                    if(method.IsConstructor&&!Has(method,"ConfigureText"))
                    foreach(var ret in code.Where(i=>i.OpCode==OpCodes.Ret).ToArray())
                    {
                        var text=type.Fields.FirstOrDefault(f=>f.Name=="textObj");if(text!=null){il.InsertBefore(ret,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(ret,Instruction.Create(OpCodes.Ldfld,text));il.InsertBefore(ret,Instruction.Create(OpCodes.Call,Helper("ConfigureText")));}
                        var button=type.Fields.FirstOrDefault(f=>f.Name=="button"&&f.FieldType.FullName=="UnityEngine.UI.Button");if(button!=null){il.InsertBefore(ret,Instruction.Create(OpCodes.Ldarg_0));il.InsertBefore(ret,Instruction.Create(OpCodes.Ldfld,button));il.InsertBefore(ret,Instruction.Create(OpCodes.Call,Helper("ConfigureButton")));}
                    }
                }
                if(type.FullName=="DebugMod.MonoBehaviours.TimeScale"&&method.Name=="OnDestroy")foreach(var i in code.Where(i=>i.Operand is MethodReference r&&r.DeclaringType.FullName=="UnityEngine.Time"&&r.Name=="set_timeScale")){i.OpCode=OpCodes.Call;i.Operand=Helper("ApplyDefaultTimeScale");}
                if((type.Name=="MenuButtonList"&&method.Name=="DoSelect")||(type.Name=="PromptMarker"&&method.Name=="Hide"))
                    foreach(var i in code.Where(i=>i.Operand is MethodReference r&&r.Name=="StartCoroutine"&&r.Parameters.Count==1&&r.Parameters[0].ParameterType.FullName=="System.Collections.IEnumerator")){i.OpCode=OpCodes.Call;i.Operand=Helper("StartWhenActive");}
                if(((type.Name=="GameMap"&&method.Name=="OnActiveSceneChanged")||(type.FullName=="QoL.Modules.SalubraKiller"&&method.Name=="SceneChanged"))&&!Has(method,"IsMenuScene"))
                {var next=method.Parameters.Last(p=>p.ParameterType.FullName=="UnityEngine.SceneManagement.Scene");Prefix(method,Instruction.Create(OpCodes.Ldarg,next),Instruction.Create(OpCodes.Call,Helper("IsMenuScene")),Instruction.Create(OpCodes.Brfalse,code[0]),Instruction.Create(OpCodes.Ret));}
            }
            assembly.Write(path+".ui");assembly.Dispose();File.Move(path+".ui",path,true);
        }
        Console.WriteLine("Adapted Debug UI focus/fonts/pages/bindings and inactive menu coroutines");
    }
}
