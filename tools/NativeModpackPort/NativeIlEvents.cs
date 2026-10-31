using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

static class NativeIlEvents
{
    public static void Build(string root)
    {
        using var resolver=new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.Combine(root,"native-stage"));
        var events=new Dictionary<string,MethodReference>();
        foreach(var path in Directory.GetFiles(Path.Combine(root,"inputs"),"*.dll",SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root,"dependencies"),"*.dll"))){
            using var input=AssemblyDefinition.ReadAssembly(path);
            foreach(var reference in input.MainModule.GetMemberReferences().OfType<MethodReference>())
                if(reference.DeclaringType.FullName.StartsWith("IL.")&&reference.Name.StartsWith("add_"))events.TryAdd(reference.FullName,reference);
        }
        var records=new List<object>();
        foreach(var reference in events.Values){
            string assembly=reference.DeclaringType.Scope.Name;
            using var definitions=AssemblyDefinition.ReadAssembly(Path.Combine(root,"api-reference/OutputFinal",assembly+".dll"),new ReaderParameters{AssemblyResolver=resolver});
            var add=definitions.MainModule.GetType(reference.DeclaringType.FullName).Methods.Single(m=>m.Name==reference.Name);
            var target=add.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldtoken).Select(i=>i.Operand).OfType<MethodReference>().Single();
            string full=reference.DeclaringType.FullName;int split=full.LastIndexOf('.');
            string ns=full[..split],cls=full[(split+1)..],name=reference.Name[4..];
            string signature="new global::System.Type[]{"+string.Join(",",target.Parameters.Select(p=>"typeof("+StaticHooks.TypeName(p.ParameterType)+")"))+"}";
            string find="typeof("+StaticHooks.TypeName(target.DeclaringType)+").GetMethod(\""+target.Name+"\", global::System.Reflection.BindingFlags.Public|global::System.Reflection.BindingFlags.NonPublic|global::System.Reflection.BindingFlags.Instance|global::System.Reflection.BindingFlags.Static, null, "+signature+", null)";
            string source=$@"namespace {ns} {{public static partial class {cls} {{
public static event global::MonoMod.Cil.ILContext.Manipulator {name} {{
add {{ global::NativeModpack.PatchRegistrations.Add(""{full}.{name}"",{find},value); }}
remove {{ global::NativeModpack.PatchRegistrations.Remove(""{full}.{name}"",value); }}
}}
}} }}";
            string directory=Path.Combine(root,"native-hook-sources",assembly);Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory,"IL_"+cls+"_"+name+".cs"),source);
            records.Add(new{Boundary=full+"."+name,Target=target.FullName,Assembly=assembly});
        }
        File.WriteAllText(Path.Combine(root,"native-il-registration-boundaries.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Generated {records.Count} fixed IL-feature registration boundaries");
    }
}
