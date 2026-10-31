using Mono.Cecil;
using Mono.Cecil.Cil;

static class NativeInputAdapters
{
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source)
    {foreach(var t in source){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
    public static void Build(string root)
    {
        string stage=Path.Combine(root,"native-stage");
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(stage);
        using var runtime=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"NativeModRuntime.dll"));
        using var loader=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"NativeModLoader.dll"));
        using var game=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"Assembly-CSharp.dll"));
        var legacy=runtime.MainModule.GetType("NativeModpack.LegacyInput");
        var bridge=loader.MainModule.GetType("NativeModpack.Loader");
        foreach(string name in new[]{"AnyRadiance1._0","DebugMod","QoL","GodSeekerPlus","Osmi","Satchel","Vasi"}){
            string path=Path.Combine(stage,name+".dll");
            using var assembly=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{AssemblyResolver=resolver,InMemory=true});
            var module=assembly.MainModule;
            foreach(var method in Types(module.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody)){
                var code=method.Body.Instructions;
                foreach(var i in code){
                    if(i.Operand is MethodReference call&&call.DeclaringType.FullName=="UnityEngine.Input"){
                        if(call.Name=="GetKeyDown"&&call.Parameters.Single().ParameterType.FullName=="UnityEngine.KeyCode")i.Operand=module.ImportReference(legacy.Methods.Single(m=>m.Name=="GetKeyDown"));
                        if(call.Name=="get_mousePosition")i.Operand=module.ImportReference(legacy.Methods.Single(m=>m.Name=="MousePosition"));
                    }
                    if(i.OpCode==OpCodes.Ldstr&&i.Operand is string text&&text.StartsWith("<EnterScene>d__"))
                        i.Operand=game.MainModule.GetType("HeroController").NestedTypes.Single(t=>t.Name.StartsWith("<EnterScene>d__")).Name;
                }
                foreach(var i in code.Where(i=>i.OpCode==OpCodes.Ldsfld&&i.Operand is FieldReference f&&f.DeclaringType.FullName=="DebugMod.BindableFunctions"&&f.Name=="TimeSlowed").ToArray()){
                    int index=code.IndexOf(i);
                    var call=code.Skip(index+1).Take(8).First(j=>j.Operand is MethodReference m&&m.DeclaringType.FullName=="System.Reflection.FieldInfo"&&(m.Name=="GetValue"||m.Name=="SetValue"));
                    string target=((MethodReference)call.Operand).Name=="GetValue"?"GetTimeSlowed":"SetTimeSlowed";
                    i.OpCode=OpCodes.Nop;i.Operand=null;call.OpCode=OpCodes.Call;call.Operand=module.ImportReference(bridge.Methods.Single(m=>m.Name==target));
                }
            }
            // AOT assemblies have no standalone executable DLL to fingerprint.
            // Keep an explicit native version label instead of opening a fake path.
            var version=name=="Vasi"?module.GetType("Vasi.VersionUtil").Methods.Single(m=>m.Name=="GetVersion"):null;
            if(version!=null){
                var getType=module.GetMemberReferences().OfType<MethodReference>().First(m=>m.DeclaringType.FullName=="System.Type"&&m.Name=="GetTypeFromHandle");
                var getAssembly=module.GetMemberReferences().OfType<MethodReference>().First(m=>m.DeclaringType.FullName=="System.Type"&&m.Name=="get_Assembly");
                version.Body=new MethodBody(version);var il=version.Body.GetILProcessor();
                il.Emit(OpCodes.Ldtoken,version.GenericParameters[0]);il.Emit(OpCodes.Call,getType);il.Emit(OpCodes.Callvirt,getAssembly);
                il.Emit(OpCodes.Call,module.ImportReference(bridge.Methods.Single(m=>m.Name=="AssemblyVersion")));il.Emit(OpCodes.Ret);
            }
            if(name=="Satchel"){
                var method=module.GetType("Satchel.AssemblyUtils").Methods.Single(m=>m.Name=="GetAssemblyVersionHash"&&m.Parameters.Count==2);
                method.Body=new MethodBody(method);var il=method.Body.GetILProcessor();
                il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,module.ImportReference(bridge.Methods.Single(m=>m.Name=="AssemblyVersion")));il.Emit(OpCodes.Ret);
            }
            assembly.Write(path+".input-adapted");assembly.Dispose();File.Move(path+".input-adapted",path,true);
        }
        Console.WriteLine("Adapted Debug keyboard/mouse, time-slow state, current iterator identity and AOT version labels");
    }
}
