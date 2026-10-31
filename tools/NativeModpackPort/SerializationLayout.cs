using Mono.Cecil;
using System.Text.Json;

static class SerializationLayout
{
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source)
    {foreach(var t in source){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
    static bool Visible(FieldDefinition f)=>!f.IsStatic&&!f.IsLiteral&&!f.IsInitOnly&&!f.IsNotSerialized&&(f.IsPublic||f.CustomAttributes.Any(a=>a.AttributeType.FullName is "UnityEngine.SerializeField" or "UnityEngine.SerializeReference"));
    public static void Check(string root,bool fix)
    {
        var changes=new List<object>();
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(Path.Combine(root,"native-stage"));
        foreach(string name in new[]{"Assembly-CSharp","PlayMaker","TeamCherry.Localization"}){
            using var original=AssemblyDefinition.ReadAssembly(Path.Combine(root,"api-reference/Vanilla",name+".dll"));
            string path=Path.Combine(root,"native-stage",name+".dll");
            using var native=AssemblyDefinition.ReadAssembly(path,new ReaderParameters{InMemory=true,AssemblyResolver=resolver});
            var originals=Types(original.MainModule.Types).ToDictionary(t=>t.FullName);
            foreach(var type in Types(native.MainModule.Types)){
                if(!originals.TryGetValue(type.FullName,out var before))continue;
                if(type.BaseType?.FullName!=before.BaseType?.FullName||type.IsSerializable!=before.IsSerializable)
                    throw new InvalidOperationException("Original serialization type hierarchy/flags changed: "+type.FullName);
                var fields=before.Fields.ToDictionary(f=>f.Name);
                foreach(var field in type.Fields){
                    bool existed=fields.TryGetValue(field.Name,out var old);
                    bool oldVisible=existed&&Visible(old!);
                    if(Visible(field)&&!oldVisible){
                        changes.Add(new{Assembly=name,Type=type.FullName,Field=field.Name,Original=existed?old!.Attributes.ToString():"new field",Native=field.Attributes.ToString()});
                        if(fix)field.IsNotSerialized=true;
                    }
                    if(oldVisible&&!Visible(field))throw new InvalidOperationException("Original scene field lost: "+field.FullName);
                }
                var expected=before.Fields.Where(Visible).Select(f=>(f.Name,f.FieldType.FullName)).ToArray();
                var retained=type.Fields.Where(f=>Visible(f)&&fields.ContainsKey(f.Name)).Select(f=>(f.Name,f.FieldType.FullName)).ToArray();
                if(!expected.SequenceEqual(retained))throw new InvalidOperationException("Original serialized field order/type changed: "+type.FullName);
            }
            if(fix){native.Write(path+".serialization");native.Dispose();File.Move(path+".serialization",path,true);}
        }
        File.WriteAllText(Path.Combine(root,"native-serialization-layout.json"),JsonSerializer.Serialize(changes,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"{changes.Count} fields added to Unity's serialized layout"+(fix?"; excluded from serialization":""));
        foreach(var item in changes)Console.WriteLine(JsonSerializer.Serialize(item));
        if(!fix&&changes.Count!=0)throw new InvalidOperationException("Unity serialization layout differs from original game");
    }
}
