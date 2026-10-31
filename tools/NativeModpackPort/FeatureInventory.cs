using Mono.Cecil;
using System.Text.Json;

static class FeatureInventory
{
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source)
    {foreach(var t in source){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
    static string[] Modules(AssemblyDefinition assembly,string baseName)
    {
        var types=Types(assembly.MainModule.Types).ToDictionary(t=>t.FullName);
        bool Descends(TypeDefinition type)
        {
            var parent=type.BaseType;
            while(parent!=null){
                if(parent.FullName==baseName)return true;
                if(!types.TryGetValue(parent.FullName,out var definition))return false;
                parent=definition.BaseType;
            }return false;
        }
        return types.Values.Where(t=>!t.IsAbstract&&Descends(t)).Select(t=>t.FullName).Order().ToArray();
    }
    public static void Check(string root)
    {
        var records=new List<object>();
        foreach(var pair in new[]{("QoL","inputs/QoL/QoL.dll","QoL.Modules.FauxMod"),("GodSeekerPlus","inputs/GodSeekerPlus.dll","GodSeekerPlus.Modules.Module")}){
            using var original=AssemblyDefinition.ReadAssembly(Path.Combine(root,pair.Item2));
            using var native=AssemblyDefinition.ReadAssembly(Path.Combine(root,"native-stage",pair.Item1+".dll"));
            var expected=Modules(original,pair.Item3);var actual=Modules(native,pair.Item3);
            if(!expected.SequenceEqual(actual))throw new InvalidOperationException("Module inventory differs from original "+pair.Item1);
            records.Add(new{Mod=pair.Item1,Count=actual.Length,Modules=actual});
            Console.WriteLine(pair.Item1+": all "+actual.Length+" original modules retained");
        }
        using var before=AssemblyDefinition.ReadAssembly(Path.Combine(root,"inputs/DebugMod.dll"));
        using var after=AssemblyDefinition.ReadAssembly(Path.Combine(root,"native-stage/DebugMod.dll"));
        string[] Commands(AssemblyDefinition assembly)=>Types(assembly.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.CustomAttributes.Any(a=>a.AttributeType.FullName=="DebugMod.BindableMethod")).Select(m=>m.FullName).Order().ToArray();
        var expectedCommands=Commands(before);var actualCommands=Commands(after);
        if(!expectedCommands.SequenceEqual(actualCommands))throw new InvalidOperationException("Debug commands differ from original");
        records.Add(new{Mod="DebugMod",Count=actualCommands.Length,Commands=actualCommands});
        Console.WriteLine("DebugMod: all "+actualCommands.Length+" original commands retained");
        File.WriteAllText(Path.Combine(root,"native-feature-inventory.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
    }
}
