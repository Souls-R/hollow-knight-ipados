using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

static class BuildOnlyIl
{
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source)
    {foreach(var t in source){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
    static bool CompileType(TypeReference type)=>type.FullName.Contains("Mono.Cecil")||type.FullName.Contains("Mono.Collections")||type.FullName.Contains("MonoMod.Cil.ILCursor")||type.FullName.Contains("MonoMod.Cil.ILLabel")||type.FullName.Contains("MonoMod.Utils.DynamicMethodDefinition");
    public static void Remove(string root)
    {
        var reports=new List<object>();
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(Path.Combine(root,"native-stage"));
        foreach(string name in new[]{"DebugMod","GodSeekerPlus","QoL","Osmi"}){
            string path=Path.Combine(root,"native-stage",name+".dll");
            using var assembly=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{InMemory=true,AssemblyResolver=resolver});
            var module=assembly.MainModule;
            var methods=Types(module.Types).SelectMany(t=>t.Methods).ToArray();
            var removed=new HashSet<MethodDefinition>();
            foreach(var method in methods){
                bool manipulator=method.Parameters.Any(p=>p.ParameterType.FullName=="MonoMod.Cil.ILContext");
                bool helper=CompileType(method.ReturnType)||method.Parameters.Any(p=>CompileType(p.ParameterType));
                if(helper){removed.Add(method);reports.Add(new{Assembly=name,Method=method.FullName,Action="remove offline IL helper"});continue;}
                if(!manipulator||!method.HasBody)continue;
                // A lease identifies this callback but never invokes it. The
                // real behavior lives in the guarded compiled variants.
                method.Body=new MethodBody(method);var il=method.Body.GetILProcessor();
                il.Emit(OpCodes.Ldstr,"Offline IL manipulator cannot run on device: "+method.FullName);
                var exceptionType=new TypeReference("System","InvalidOperationException",module,module.TypeSystem.CoreLibrary);
                var constructor=new MethodReference(".ctor",module.TypeSystem.Void,exceptionType){HasThis=true};
                constructor.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                il.Emit(OpCodes.Newobj,constructor);il.Emit(OpCodes.Throw);
                reports.Add(new{Assembly=name,Method=method.FullName,Action="registration identity only; compiled variant executes behavior"});
            }
            foreach(var method in methods.Where(m=>!removed.Contains(m)&&m.HasBody))
                foreach(var instruction in method.Body.Instructions)
                    if(instruction.Operand is MethodReference reference && removed.Any(m=>m.FullName==reference.FullName))
                        throw new InvalidOperationException("Live reference to offline helper: "+method.FullName+" -> "+reference.FullName);
            foreach(var method in removed)method.DeclaringType.Methods.Remove(method);
            foreach(var type in Types(module.Types))foreach(var field in type.Fields.Where(f=>CompileType(f.FieldType)).ToArray()){
                if(Types(module.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).Any(m=>m.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.FullName==field.FullName)))
                    throw new InvalidOperationException("Runtime use of build-time field: "+field.FullName);
                type.Fields.Remove(field);
            }
            string temp=path+".pruned";assembly.Write(temp);assembly.Dispose();File.Move(temp,path,true);
        }
        File.WriteAllText(Path.Combine(root,"native-build-time-il-removal.json"),JsonSerializer.Serialize(reports,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Separated {reports.Count} offline manipulation/helper methods from native runtime");
    }
}
