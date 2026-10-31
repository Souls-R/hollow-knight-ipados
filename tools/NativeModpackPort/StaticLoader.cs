using Mono.Cecil;
using Mono.Cecil.Cil;

static class StaticLoader
{
    public static void Build(string root)
    {
        string stage=Path.Combine(root,"native-stage"),path=Path.Combine(stage,"Assembly-CSharp.dll");
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(stage);
        using var assembly=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{InMemory=true,AssemblyResolver=resolver});
        using var support=AssemblyDefinition.ReadAssembly(Path.Combine(stage,"NativeModLoader.dll"));
        var module=assembly.MainModule;
        var loader=module.GetType("Modding.ModLoader");
        var iterator=loader.NestedTypes.Single(t=>t.Name.StartsWith("<LoadModsInit>"));
        var method=iterator.Methods.Single(m=>m.Name=="MoveNext");
        var instructions=method.Body.Instructions;
        var begin=instructions.Single(i=>i.Operand is MethodReference m&&m.Name=="get_operatingSystemFamily");
        var end=instructions.First(i=>i.Offset>begin.Offset&&i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference m&&m.DeclaringType.FullName=="System.Collections.Generic.List`1<System.String>");
        int start=instructions.IndexOf(begin),stop=instructions.IndexOf(end);
        var removed=instructions.Skip(start).Take(stop-start).ToHashSet();
        foreach(var handler in method.Body.ExceptionHandlers.ToArray())if(removed.Contains(handler.TryStart)){
            if(!removed.Contains(handler.HandlerStart))throw new InvalidOperationException("Loader exception boundary spans native discovery");
            method.Body.ExceptionHandlers.Remove(handler);
        }
        foreach(var instruction in instructions.Where(i=>!removed.Contains(i))){
            if(instruction.Operand is Instruction target&&removed.Contains(target))throw new InvalidOperationException("Unexpected incoming discovery branch");
            if(instruction.Operand is Instruction[] targets&&targets.Any(removed.Contains))throw new InvalidOperationException("Unexpected incoming discovery switch");
        }
        foreach(var instruction in removed)instructions.Remove(instruction);
        method.Body.GetILProcessor().InsertBefore(end,Instruction.Create(OpCodes.Call,module.ImportReference(support.MainModule.GetType("NativeModpack.Loader").Methods.Single(m=>m.Name=="RegisterAll"))));
        // No desktop assembly resolver is retained in the native loading path.
        var closure=loader.NestedTypes.Single(t=>t.Name=="<>c__DisplayClass16_0");
        foreach(var i in instructions.Where(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName==closure.FullName).ToArray()){
            int index=instructions.IndexOf(i);
            if(i.OpCode!=OpCodes.Newobj||instructions[index-1].OpCode!=OpCodes.Ldarg_0||instructions[index+1].OpCode!=OpCodes.Stfld)throw new InvalidOperationException("Unexpected discovery closure use");
            instructions[index-1].OpCode=OpCodes.Nop;i.OpCode=OpCodes.Nop;i.Operand=null;instructions[index+1].OpCode=OpCodes.Nop;instructions[index+1].Operand=null;
        }
        foreach(var f in iterator.Fields.Where(f=>f.FieldType.FullName==closure.FullName).ToArray())iterator.Fields.Remove(f);
        loader.NestedTypes.Remove(closure);
        var preload=module.GetType("Modding.Preloader").Methods.Single(m=>m.Name=="Preload");
        preload.Body=new MethodBody(preload);var il=preload.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);foreach(var p in preload.Parameters)il.Emit(OpCodes.Ldarg,p);
        il.Emit(OpCodes.Call,module.ImportReference(support.MainModule.GetType("NativeModpack.Loader").Methods.Single(m=>m.Name=="Preload")));il.Emit(OpCodes.Ret);
        assembly.Write(path+".static-loader");assembly.Dispose();File.Move(path+".static-loader",path,true);
        Console.WriteLine("Installed typed native factories and full-scene preload path without runtime DLL scanning");
    }
}
