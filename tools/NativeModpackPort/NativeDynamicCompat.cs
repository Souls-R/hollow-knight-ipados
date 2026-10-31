using Mono.Cecil;
using Mono.Cecil.Cil;

static class NativeDynamicCompat
{
    public static void Build(string root)
    {
        string stage=Path.Combine(root,"native-stage");
        using var support=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"NativeModRuntime.dll"));
        string path=Path.Combine(stage,"Osmi.dll");
        using var assembly=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{InMemory=true});
        var module=assembly.MainModule;
        var bridge=support.MainModule.GetType("NativeModpack.ObjectReflection");
        var cloneType=module.GetType("Osmi.Utils.CloneUtil");
        var clone=cloneType.Methods.Single(m=>m.Name=="CreateMemberwiseClone");
        clone.Body=new MethodBody(clone);var il=clone.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,module.ImportReference(bridge.Methods.Single(m=>m.Name=="Clone")));
        il.Emit(OpCodes.Isinst,clone.GenericParameters[0]);il.Emit(OpCodes.Ret);
        var initializer=cloneType.Methods.Single(m=>m.Name==".cctor");
        initializer.Body=new MethodBody(initializer);initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        cloneType.NestedTypes.Clear();cloneType.Fields.Clear();
        var child=module.GetType("Osmi.ChildRefs.ChildRef").Methods.Single(m=>m.Name=="Compile");
        child.Body=new MethodBody(child);il=child.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Call,module.ImportReference(bridge.Methods.Single(m=>m.Name=="ChildReferences")));il.Emit(OpCodes.Ret);
        foreach(var nested in child.DeclaringType.NestedTypes.Where(t=>t.Name.StartsWith("<>c__DisplayClass")).ToArray())
            child.DeclaringType.NestedTypes.Remove(nested);
        string temp=path+".native-objects";assembly.Write(temp);assembly.Dispose();File.Move(temp,path,true);
        Console.WriteLine("Replaced Osmi clone and child reference code generation with native reflection");
    }
}
