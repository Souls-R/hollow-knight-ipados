using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

static class NativeCompat
{
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source)
    {foreach(var t in source){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
    public static void Build(string root)
    {
        string stage=Path.Combine(root,"native-stage");
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(stage);
        var assemblies=Directory.GetFiles(stage,"*.dll").Select(path=>AssemblyDefinition.ReadAssembly(path,new ReaderParameters{AssemblyResolver=resolver,InMemory=true})).ToDictionary(a=>a.Name.Name);
        var game=assemblies["Assembly-CSharp"];
        var utility=assemblies["MonoMod.Utils"].MainModule;
        var runtime=assemblies["NativeModRuntime"].MainModule;
        var changes=new List<string>();
        void ForwardGeneric(MethodDefinition method,MethodDefinition target)
        {
            method.Body=new MethodBody(method);var il=method.Body.GetILProcessor();
            foreach(var p in method.Parameters)il.Emit(OpCodes.Ldarg,p);
            var reference=method.Module.ImportReference(target);
            if(method.HasGenericParameters){var closed=new GenericInstanceMethod(reference);foreach(var p in method.GenericParameters)closed.GenericArguments.Add(p);reference=closed;}
            il.Emit(OpCodes.Call,reference);il.Emit(OpCodes.Ret);changes.Add(method.FullName+" -> "+target.FullName);
        }
        var reflection=game.MainModule.GetType("Modding.ReflectionHelper");
        var bridge=utility.GetType("MonoMod.Utils.ReflectionBridge");
        foreach(var method in reflection.Methods.Where(m=>m.Name.StartsWith("GetInstanceField")||m.Name.StartsWith("GetStaticField")||m.Name.StartsWith("GetInstanceProperty")||m.Name.StartsWith("GetStaticProperty")))
            if(method.HasGenericParameters&&method.ReturnType.FullName=="System.Delegate")ForwardGeneric(method,bridge.Methods.Single(m=>m.Name==method.Name));
        var fast=reflection.Methods.Single(m=>m.Name=="GetFastReflectionDelegate");
        ForwardGeneric(fast,utility.GetType("MonoMod.Utils.FastReflectionHelper").Methods.Single(m=>m.Name=="GetFastInvoker"&&m.Parameters.Count==1));
        var vasi=assemblies["Vasi"].MainModule.GetType("Vasi.Mirror");
        var mirrorMap=new Dictionary<string,string>{["CreateGetStaticFieldDelegate"]="GetStaticFieldGetter",["CreateGetFieldDelegate"]="GetInstanceFieldGetter",["CreateSetFieldDelegate"]="GetInstanceFieldSetter",["CreateSetStaticFieldDelegate"]="GetStaticFieldSetter"};
        foreach(var pair in mirrorMap)ForwardGeneric(vasi.Methods.Single(m=>m.Name==pair.Key),bridge.Methods.Single(m=>m.Name==pair.Value));
        foreach(string name in new[]{"CreateGetFieldRefDelegate","CreateGetStaticFieldRefDelegate"})
            ForwardGeneric(vasi.Methods.Single(m=>m.Name==name),runtime.GetType("NativeModpack.ReferenceFields").Methods.Single(m=>m.Name==(name.Contains("Static")?"GetStatic":"Get")));
        // Type names were moved, not redesigned: keep the old text layout and
        // all its measurements by binding to the game's legacy TMProOld types.
        var typeLookup=assemblies.Values.SelectMany(a=>Types(a.MainModule.Types)).GroupBy(t=>t.FullName).ToDictionary(g=>g.Key,g=>g.First());
        var consumers=new[]{"AnyRadiance1._0","DebugMod","QoL","GodSeekerPlus","Satchel","Osmi","Vasi"};
        foreach(string name in consumers){
            var assembly=assemblies[name];
            foreach(var type in assembly.MainModule.GetTypeReferences())if(type.Namespace=="TMPro"){
                string wanted="TMProOld."+type.Name;
                if(!typeLookup.TryGetValue(wanted,out var target))throw new TypeLoadException(wanted);
                type.Namespace="TMProOld";type.Scope=assembly.MainModule.ImportReference(target).Scope;changes.Add(name+": "+wanted);
            }
            foreach(var method in Types(assembly.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody))foreach(var i in method.Body.Instructions)
                if(i.Operand is MethodReference call&&call.DeclaringType.FullName=="System.Reflection.Assembly"&&call.Name=="get_Location"){
                    i.OpCode=OpCodes.Call;i.Operand=assembly.MainModule.ImportReference(runtime.GetType("NativeModpack.Paths").Methods.Single(m=>m.Name=="AssemblyLocation"));changes.Add(method.FullName+": stable native profile path");
                }
        }
        // A legacy one-argument event dispatch still means an unscoped event.
        var events=game.MainModule.GetType("EventRegister");
        if(!events.Methods.Any(m=>m.Name=="SendEvent"&&m.Parameters.Count==1)){
            var target=events.Methods.Where(m=>m.Name=="SendEvent"&&m.Parameters.Count==2&&m.Parameters[0].ParameterType.FullName=="System.String").Single();
            if(target.ReturnType.MetadataType!=MetadataType.Void||target.Parameters[1].ParameterType.IsValueType)throw new InvalidOperationException("Event dispatch ABI changed");
            var method=new MethodDefinition("SendEvent",MethodAttributes.Public|MethodAttributes.Static|MethodAttributes.HideBySig,game.MainModule.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("name",ParameterAttributes.None,game.MainModule.TypeSystem.String));events.Methods.Add(method);
            var il=method.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldnull);il.Emit(OpCodes.Call,target);il.Emit(OpCodes.Ret);changes.Add("EventRegister.SendEvent(string) compatibility overload");
        }
        var writable=new HashSet<string>(consumers.Concat(new[]{"Assembly-CSharp","PlayMaker","TeamCherry.Localization","NativeModRuntime","MonoMod.Utils","MonoMod.RuntimeDetour","MMHOOK_Assembly-CSharp","MMHOOK_PlayMaker"}));
        foreach(var assembly in assemblies.Values.Where(a=>writable.Contains(a.Name.Name))){
            foreach(var reference in assembly.MainModule.AssemblyReferences)if(assemblies.TryGetValue(reference.Name,out var actual)){
                reference.Version=actual.Name.Version;reference.PublicKeyToken=actual.Name.PublicKeyToken;
            }
            string temp=Path.Combine(stage,assembly.Name.Name+".dll.compat");assembly.Write(temp);assembly.Dispose();File.Move(temp,Path.Combine(stage,assembly.Name.Name+".dll"),true);
        }
        foreach(var assembly in assemblies.Values.Where(a=>!writable.Contains(a.Name.Name)))assembly.Dispose();
        File.WriteAllText(Path.Combine(root,"native-compatibility-changes.json"),JsonSerializer.Serialize(changes,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Applied {changes.Count} native API compatibility substitutions");
    }
}
