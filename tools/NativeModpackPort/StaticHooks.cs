using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text;
using System.Text.Json;

// Emits strongly typed, AOT-compilable continuations for the exact hook set.
// Original methods are preserved, including multiple calls to orig and nested
// reentry. No RuntimeDetour, emitted IL or executable-memory patching at runtime.
static class StaticHooks
{
    internal static string TypeName(TypeReference type)
    {
        if(type.MetadataType==MetadataType.Void) return "void";
        if(type is ByReferenceType br) return TypeName(br.ElementType);
        if(type is ArrayType a) return TypeName(a.ElementType)+"["+new string(',',a.Rank-1)+"]";
        if(type is GenericInstanceType g) return "global::"+g.ElementType.FullName.Split('`')[0].Replace('/','.')+"<"+string.Join(",",g.GenericArguments.Select(TypeName))+">";
        if(type is GenericParameter) throw new NotSupportedException("Generic hook: "+type.FullName);
        return "global::"+type.FullName.Replace('/','.');
    }
    static string Arg(ParameterDefinition p,int i,bool declaration)
    {
        var prefix=p.ParameterType.IsByReference?(p.IsOut?"out ":"ref "):"";
        return prefix+(declaration?TypeName(p.ParameterType)+" ":"")+"p"+i;
    }
    static MethodDefinition Preserve(MethodDefinition method,string name)
    {
        if(method.HasGenericParameters || method.DeclaringType.HasGenericParameters || method.IsConstructor)
            throw new NotSupportedException("Unsupported native hook target: "+method.FullName);
        var copy=new MethodDefinition(name,MethodAttributes.Public|MethodAttributes.HideBySig|(method.IsStatic?MethodAttributes.Static:0),method.ReturnType);
        foreach(var p in method.Parameters) copy.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,p.ParameterType));
        copy.Body=method.Body;
        method.DeclaringType.Methods.Add(copy);
        return copy;
    }
    public static void Build(string root,bool refresh=false)
    {
        var stage=Path.Combine(root,"native-stage");
        if(Directory.Exists(stage)&&!refresh) throw new IOException("Preserve the existing native-stage before rebuilding");
        Directory.CreateDirectory(stage);
        if(!refresh){
            foreach(var path in Directory.GetFiles(Path.Combine(root,"api-reference/Vanilla"),"*.dll")) File.Copy(path,Path.Combine(stage,Path.GetFileName(path)));
            foreach(var path in Directory.GetFiles(Path.Combine(root,"api-reference/OutputFinal"),"*.dll")) File.Copy(path,Path.Combine(stage,Path.GetFileName(path)),true);
            foreach(var path in Directory.GetFiles(Path.Combine(root,"dependencies"),"*.dll")) File.Copy(path,Path.Combine(stage,Path.GetFileName(path)),true);
        }
        using var resolver=new DefaultAssemblyResolver(); resolver.AddSearchDirectory(stage);
        var parameters=new ReaderParameters{AssemblyResolver=resolver,InMemory=true};
        var loaded=new Dictionary<string,AssemblyDefinition>();
        AssemblyDefinition Load(string name)
        {
            if(!loaded.TryGetValue(name,out var value)) loaded[name]=value=AssemblyDefinition.ReadAssembly(Path.Combine(stage,name+".dll"),parameters);
            return value;
        }
        var inputs=Directory.GetFiles(Path.Combine(root,"inputs"),"*.dll",SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root,"dependencies"),"*.dll")).ToArray();
        var registrations=new Dictionary<string,MethodReference>();
        foreach(var path in inputs) {
            using var input=AssemblyDefinition.ReadAssembly(path,parameters);
            foreach(var method in input.MainModule.GetMemberReferences().OfType<MethodReference>())
                if(method.DeclaringType.FullName.StartsWith("On.") && method.Name.StartsWith("add_"))
                    registrations.TryAdd(method.FullName,method);
        }
        var generated=Path.Combine(root,"native-hook-sources"); Directory.CreateDirectory(generated);
        var manifest=new List<object>();
        var previous=refresh?JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"native-static-hooks.json"))).RootElement.EnumerateArray().ToDictionary(x=>x.GetProperty("Hook").GetString()!,x=>x.Clone()):null;
        foreach(var pair in registrations) {
            var registration=pair.Value;
            var legacyLanguage=registration.DeclaringType.FullName=="On.Language.Language" && registration.Name=="add_DoSwitch";
            MethodDefinition target;
            if(refresh){
                string fullTarget=previous![registration.FullName].GetProperty("Target").GetString()!;
                IEnumerable<TypeDefinition> Walk(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in Walk(t.NestedTypes))yield return n;}}
                target=new[]{"Assembly-CSharp","PlayMaker","TeamCherry.Localization"}.Select(Load).SelectMany(a=>Walk(a.MainModule.Types)).SelectMany(t=>t.Methods).Single(m=>m.FullName==fullTarget);
            }else{
                MethodDefinition add;
                if(legacyLanguage) add=Load("MMHOOK_TeamCherry.Localization").MainModule.GetType("On.TeamCherry.Localization.Language").Methods.Single(m=>m.Name=="add_DoSwitch");
                else add=registration.Resolve() ?? throw new MissingMethodException(registration.FullName);
                var token=add.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldtoken).Select(i=>i.Operand).OfType<MethodReference>().Single();
                var targetAssembly=Load(token.DeclaringType.Scope.Name);
                var type=targetAssembly.MainModule.GetType(token.DeclaringType.FullName);
                target=type.Methods.Single(m=>m.Name==token.Name&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(token.Parameters.Select(p=>p.ParameterType.FullName)));
            }
            var targetType=target.DeclaringType;
            if(!target.HasBody) throw new InvalidOperationException("Hook target has no IL: "+target.FullName);
            var module=target.Module;
            var nativeName=refresh?previous![registration.FullName].GetProperty("Original").GetString()!:"NativeOriginal_"+target.MetadataToken.RID;
            if(!refresh)Preserve(target,nativeName);
            var outputAssembly=registration.DeclaringType.Scope.Name;
            var directory=Path.Combine(generated,outputAssembly); Directory.CreateDirectory(directory);
            var full=registration.DeclaringType.FullName;
            var split=full.LastIndexOf('.'); var ns=full[..split]; var cls=full[(split+1)..];
            var eventName=registration.Name[4..];
            var origName="orig_"+eventName; var hookName="hook_"+eventName;
            var ps=target.Parameters.ToList();
            if(!target.IsStatic) ps.Insert(0,new ParameterDefinition("self",ParameterAttributes.None,targetType));
            var legacyPs=ps.ToList();
            if(target.Name=="SetupNewSequence" && target.DeclaringType.Name=="BossSequenceController") legacyPs.RemoveAt(legacyPs.Count-1);
            if(legacyLanguage) legacyPs[0]=new ParameterDefinition("language",ParameterAttributes.None,Load("Assembly-CSharp").MainModule.GetType("Language.LanguageCode"));
            var declarations=string.Join(", ",legacyPs.Select((p,i)=>Arg(p,i,true)));
            var arguments=string.Join(", ",legacyPs.Select((p,i)=>Arg(p,i,false)));
            var actualDeclarations=string.Join(", ",ps.Select((p,i)=>Arg(p,i,true)));
            var ret=TypeName(target.ReturnType); var isVoid=target.ReturnType.MetadataType==MetadataType.Void;
            var emitReturn=isVoid?"":"return ";
            var key="n"+target.MetadataToken.RID;
            var source=new StringBuilder();
            source.AppendLine($"namespace {ns} {{ public static partial class {cls} {{");
            source.AppendLine($"public delegate {ret} {origName}({declarations});");
            source.AppendLine($"public delegate {ret} {hookName}({origName} orig{(declarations.Length>0?", "+declarations:"")});");
            var first=target.IsStatic?0:1;
            bool extra=ps.Count!=legacyPs.Count;
            string OriginalCall(string prefix) {
                var originalArgs=string.Join(", ",ps.Skip(first).Select((p,j)=> {
                var i=j+first;
                if(i>=legacyPs.Count)return "p"+i;
                if(legacyLanguage)return "("+TypeName(p.ParameterType)+")(int)"+prefix+i;
                return Arg(p,i,false).Replace("p"+i,prefix+i);
                }));
                return (target.IsStatic?TypeName(targetType):prefix+"0")+"."+nativeName+"("+originalArgs+")";
            }
            source.AppendLine($"static readonly object {key}sync=new object(); static {hookName} {key}handlers;");
            source.AppendLine(extra?$"static volatile global::System.Delegate[] {key}snapshot;":$"static volatile {origName} {key}chain={key}Base;");
            source.AppendLine($"public static event {hookName} {eventName} {{ add {{ lock({key}sync) {{ {key}handlers+=value; {key}Rebuild(); }} }} remove {{ lock({key}sync) {{ {key}handlers-=value; {key}Rebuild(); }} }} }}");
            source.AppendLine($"static void {key}Rebuild() {{");
            if(extra)source.AppendLine($"{key}snapshot={key}handlers==null?null:{key}handlers.GetInvocationList();");
            else {
                source.AppendLine($"{origName} chain={key}Base; if({key}handlers!=null) foreach(var entry in {key}handlers.GetInvocationList()) {{ var downstream=chain; var callback=({hookName})entry;");
                source.AppendLine($"chain=({declarations})=>{{ {emitReturn}callback(downstream{(arguments.Length>0?", "+arguments:"")}); }}; }} {key}chain=chain;");
            }
            source.AppendLine("}");
            if(!extra)source.AppendLine($"static {ret} {key}Base({declarations}) {{ {emitReturn}{OriginalCall("p")}; }}");
            source.AppendLine($"public static {ret} NativeDispatch_{eventName}({actualDeclarations}) {{");
            if(extra){
                string qs=string.Join(", ",legacyPs.Select((p,i)=>Arg(p,i,true).Replace(" p"+i," q"+i)));
                string qa=string.Join(", ",legacyPs.Select((p,i)=>Arg(p,i,false).Replace("p"+i,"q"+i)));
                source.AppendLine($"{origName} chain=({qs})=>{{ {emitReturn}{OriginalCall("q")}; }};");
                source.AppendLine($"var snapshot={key}snapshot; if(snapshot!=null) foreach(var entry in snapshot) {{ var downstream=chain; var callback=({hookName})entry; chain=({qs})=>{{ {emitReturn}callback(downstream{(qa.Length>0?", "+qa:"")}); }}; }}");
            }
            var dispatchArgs=legacyLanguage?"(global::Language.LanguageCode)(int)p0":arguments;
            source.AppendLine($"{emitReturn}{(extra?"chain":key+"chain")}({dispatchArgs}); }}");
            source.AppendLine("} }");
            File.WriteAllText(Path.Combine(directory,cls+"_"+eventName+".cs"),source.ToString());
            if(refresh)continue;
            var asmRef=module.AssemblyReferences.FirstOrDefault(r=>r.Name==outputAssembly);
            if(asmRef==null) { asmRef=new AssemblyNameReference(outputAssembly,new Version(0,0,0,0)); module.AssemblyReferences.Add(asmRef); }
            var hookType=new TypeReference(ns,cls,module,asmRef);
            var dispatch=new MethodReference("NativeDispatch_"+eventName,module.ImportReference(target.ReturnType),hookType){HasThis=false};
            foreach(var p in ps) dispatch.Parameters.Add(new ParameterDefinition(module.ImportReference(p.ParameterType)));
            target.Body=new MethodBody(target);
            var il=target.Body.GetILProcessor();
            if(!target.IsStatic) il.Emit(OpCodes.Ldarg_0);
            foreach(var p in target.Parameters) il.Emit(OpCodes.Ldarg,p);
            il.Emit(OpCodes.Call,dispatch); il.Emit(OpCodes.Ret);
            // The emitted C# must be able to call the original directly.
            targetType.IsPublic=true;
            manifest.Add(new{Hook=registration.FullName,Target=target.FullName,Original=nativeName,GeneratedAssembly=outputAssembly,RetainedNativeParameters=ps.Count,LegacyParameters=legacyPs.Count});
        }
        foreach(var value in loaded.Values) {
            if(refresh){value.Dispose();continue;}
            var output=Path.Combine(stage,value.Name.Name+".dll.native"); value.Write(output); value.Dispose();
            File.Move(output,Path.Combine(stage,value.Name.Name+".dll"),true);
        }
        if(!refresh)File.WriteAllText(Path.Combine(root,"native-static-hooks.json"),JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(refresh?$"Refreshed {registrations.Count} durable native hook continuations":$"Generated {manifest.Count} durable native hook continuations; original bodies preserved");
    }
}
