using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;

if(args.Length<2) throw new ArgumentException("audit <modpack-root>");
var root=Path.GetFullPath(args[1]);
if(args[0]=="serialization-audit") { SerializationLayout.Check(root,false); return; }
if(args[0]=="serialization-fix") { SerializationLayout.Check(root,true); return; }
if(args[0]=="inventory") { FeatureInventory.Check(root); return; }
if(args[0]=="hooks") { StaticHooks.Build(root); return; }
if(args[0]=="hook-sources") { StaticHooks.Build(root,true); return; }
if(args[0]=="fixed") { FixedPatches.Build(root); return; }
if(args[0]=="compat") { NativeCompat.Build(root); return; }
if(args[0]=="dynamic-compat") { NativeDynamicCompat.Build(root); return; }
if(args[0]=="legacy-compat") { NativeLegacyCompat.Build(root); return; }
if(args[0]=="static-loader") { StaticLoader.Build(root); return; }
if(args[0]=="finalize") { NativeFinalize.Build(root); return; }
if(args[0]=="input-adapters") { NativeInputAdapters.Build(root); return; }
if(args[0]=="scoped-types") {
    foreach(var file in Directory.GetFiles(Path.Combine(root,"native-stage"),"*.dll").Where(p=>!Path.GetFileName(p).StartsWith("MMHOOK_Team"))){
        using var asm=AssemblyDefinition.ReadAssembly(file);
        foreach(var type in asm.MainModule.GetTypeReferences().Where(t=>t.Scope.Name==args[2]))Console.WriteLine(asm.Name.Name+" -> "+type.FullName);
    }return;
}
if(args[0]=="attribute-refs") {
    using var resolver2=new DefaultAssemblyResolver();resolver2.AddSearchDirectory(Path.Combine(root,"native-stage"));
    foreach(string name in new[]{"Assembly-CSharp","PlayMaker","mscorlib"}){
        using var asm=AssemblyDefinition.ReadAssembly(Path.Combine(root,"native-stage",name+".dll"),new ReaderParameters{AssemblyResolver=resolver2});
        foreach(var r in asm.MainModule.GetMemberReferences().OfType<MethodReference>().Where(m=>m.DeclaringType.FullName=="System.Attribute"))
            Console.WriteLine(name+": "+r.FullName+" type scope="+r.DeclaringType.Scope+" result="+r.ReturnType.Scope+" HasThis="+r.HasThis+" CC="+r.CallingConvention+" resolves="+r.Resolve());
        if(name=="mscorlib")foreach(var m in asm.MainModule.GetType("System.Attribute").Methods.Where(m=>m.IsConstructor))Console.WriteLine("Definition: "+m.FullName+" HasThis="+m.HasThis+" CC="+m.CallingConvention);
    }return;
}
if(args[0]=="find-type") {
    foreach(var file in Directory.GetFiles(Path.Combine(root,"native-stage"),"*.dll")){
        using var asm=AssemblyDefinition.ReadAssembly(file);
        foreach(var type in asm.MainModule.Types.Where(t=>t.FullName.Contains(args[2]))) {
            Console.WriteLine(asm.Name.Name+" "+type.FullName);
            foreach(var method in type.Methods)Console.WriteLine(method.FullName);
        }
    }return;
}
if(args[0]=="il-events") { NativeIlEvents.Build(root); return; }
if(args[0]=="strip-build-il") { BuildOnlyIl.Remove(root); return; }
if(args[0]=="signatures") {
    using var asm=AssemblyDefinition.ReadAssembly(Path.Combine(root,"native-stage",args[2]+".dll"));
    IEnumerable<TypeDefinition> Walk(IEnumerable<TypeDefinition> source){foreach(var t in source){yield return t;foreach(var n in Walk(t.NestedTypes))yield return n;}}
    foreach(var type in Walk(asm.MainModule.Types).Where(t=>t.FullName==args[3])){
        foreach(var method in type.Methods)Console.WriteLine(method.FullName);
        foreach(var field in type.Fields)Console.WriteLine(field.FullName);
    }return;
}
if(args[0]=="il") {
    using var asm=AssemblyDefinition.ReadAssembly(Path.Combine(root,"native-stage",args.Length>4?args[4]+".dll":"Assembly-CSharp.dll"));
    IEnumerable<TypeDefinition> Walk(IEnumerable<TypeDefinition> source){foreach(var t in source){yield return t;foreach(var n in Walk(t.NestedTypes))yield return n;}}
    foreach(var t in Walk(asm.MainModule.Types).Where(t=>t.FullName==args[2]||t.FullName.Contains(args[2])))
    foreach(var m in t.Methods.Where(m=>m.Name==args[3])){
        Console.WriteLine(m.FullName);if(m.HasBody)foreach(var i in m.Body.Instructions)Console.WriteLine(i);
    }
    return;
}
if(args[0]=="uses") {
    using var asm=AssemblyDefinition.ReadAssembly(Path.Combine(root,"native-stage",args[2]+".dll"));
    IEnumerable<TypeDefinition> Walk(IEnumerable<TypeDefinition> source){foreach(var t in source){yield return t;foreach(var n in Walk(t.NestedTypes))yield return n;}}
    foreach(var m in Walk(asm.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody))
    foreach(var i in m.Body.Instructions)if(i.Operand!=null&&i.Operand.ToString()!.Contains(args[3]))Console.WriteLine(m.FullName+" => "+i);
    return;
}
using var resolver=new DefaultAssemblyResolver();
bool native=args[0]=="audit-native";
if(native)resolver.AddSearchDirectory(Path.Combine(root,"native-stage"));
foreach(var path in new[]{"api-reference/OutputFinal","api-reference/Vanilla","dependencies"}) resolver.AddSearchDirectory(Path.Combine(root,path));
var options=new ReaderParameters {AssemblyResolver=resolver,InMemory=true};
var inputs=Directory.GetFiles(Path.Combine(root,"inputs"),"*.dll",SearchOption.AllDirectories)
    .Concat(Directory.GetFiles(Path.Combine(root,"dependencies"),"*.dll"))
    .Concat(new[]{Path.Combine(root,"inputs/AnyRadiance1.dll"),Path.Combine(root,"inputs/DebugMod.dll")})
    .Select(Path.GetFullPath).Where(File.Exists).Distinct().ToArray();
if(native)inputs=new[]{"AnyRadiance1._0","DebugMod","GodSeekerPlus","QoL","Osmi","Satchel","Vasi","Assembly-CSharp","PlayMaker","TeamCherry.Localization","MonoMod.Utils","MonoMod.RuntimeDetour","MonoMod.Backports","NativeModRuntime","NativeModLoader","MMHOOK_Assembly-CSharp","MMHOOK_PlayMaker"}.Select(name=>Path.Combine(root,"native-stage",name+".dll")).ToArray();
var reports=new List<object>();
int failures=0;
var hooks=new Dictionary<string,object>();
foreach(var path in inputs) {
    using var assembly=AssemblyDefinition.ReadAssembly(path,options);
    var unresolved=new List<object>();
    var nativeHooks=new List<string>();
    foreach(var reference in assembly.MainModule.GetMemberReferences()) {
        // The CLR supplies multidimensional-array accessors; they have no
        // ordinary MethodDefinition for Cecil to resolve.
        if(native && reference is MethodReference arrayMethod && arrayMethod.DeclaringType is ArrayType && new[]{".ctor","Get","Set","Address"}.Contains(arrayMethod.Name))continue;
        if(reference.DeclaringType.FullName.StartsWith("On.") && reference.Name.StartsWith("add_")) {
            nativeHooks.Add(reference.FullName);
            try {
                if(reference is MethodReference method && method.Resolve() is {} target) {
                    var original=target.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>()
                        .FirstOrDefault(m=>m.DeclaringType.Name!="HookEndpointManager" && m.Name!="GetMethodFromHandle");
                    var token=target.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldtoken).Select(i=>i.Operand).OfType<MethodReference>().FirstOrDefault();
                    if(token!=null) hooks[reference.FullName]=new {Assembly=token.DeclaringType.Scope.Name,Type=token.DeclaringType.FullName,Method=token.FullName};
                }
            } catch(Exception e) {unresolved.Add(new{Member=reference.FullName,Reason=e.GetType().Name});}
        }
        try {
            object? member=reference switch {MethodReference m=>m.Resolve(),FieldReference f=>f.Resolve(),_=>null};
            if(member==null) unresolved.Add(new{Member=reference.FullName,Reason="unresolved"});
        } catch(Exception e) {unresolved.Add(new{Member=reference.FullName,Reason=e.GetType().Name});}
    }
    reports.Add(new{Assembly=assembly.Name.FullName,NativeHooks=nativeHooks,Unresolved=unresolved});
    failures+=unresolved.Count;
    Console.WriteLine($"{assembly.Name.Name}: {nativeHooks.Count} On registrations, {unresolved.Count} references need adaptation");
}
var jsonOptions=new JsonSerializerOptions {WriteIndented=true};
File.WriteAllText(Path.Combine(root,native?"native-final-member-audit.json":"native-member-audit.json"),JsonSerializer.Serialize(reports,jsonOptions));
if(!native)File.WriteAllText(Path.Combine(root,"native-hook-plan.json"),JsonSerializer.Serialize(hooks,jsonOptions));
Console.WriteLine($"Resolved {hooks.Count} hook registration boundaries against the current Unity 6 API");
if(native && failures!=0)throw new InvalidOperationException($"Native member audit has {failures} unresolved references");
