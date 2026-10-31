using Mono.Cecil;
using Mono.Cecil.Cil;

static class NativeLegacyCompat
{
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source)
    {foreach(var t in source){yield return t;foreach(var n in Types(t.NestedTypes))yield return n;}}
    public static void Build(string root)
    {
        string stage=Path.Combine(root,"native-stage");
        using var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(stage);
        var assemblies=Directory.GetFiles(stage,"*.dll").Select(p=>AssemblyDefinition.ReadAssembly(p,new ReaderParameters{AssemblyResolver=resolver,InMemory=true})).ToDictionary(a=>a.Name.Name);
        var game=assemblies["Assembly-CSharp"].MainModule;
        var satchel=assemblies["Satchel"].MainModule;
        foreach(var reference in satchel.GetTypeReferences().Where(t=>t.FullName=="Encryption")){
            var target=assemblies["TeamCherry.SharedUtils"].MainModule.GetType("TeamCherry.SharedUtils.Encryption");
            reference.Namespace=target.Namespace;reference.Scope=satchel.ImportReference(target).Scope;
        }
        var pool=game.GetType("PersonalObjectPool");
        var field=pool.Fields.Single(f=>f.Name=="startupPool");
        var element=((GenericInstanceType)field.FieldType).GenericArguments.Single();
        var array=new ArrayType(element);
        var getter=pool.Methods.FirstOrDefault(m=>m.Name=="NativeStartupPoolArray");
        if(getter==null){
            getter=new MethodDefinition("NativeStartupPoolArray",MethodAttributes.Public|MethodAttributes.Static,array);
            getter.Parameters.Add(new ParameterDefinition(pool));pool.Methods.Add(getter);
            var toArray=new MethodReference("ToArray",new ArrayType(((GenericInstanceType)field.FieldType).ElementType.Resolve().GenericParameters[0]),field.FieldType){HasThis=true};
            var il=getter.Body.GetILProcessor();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,field);il.Emit(OpCodes.Callvirt,toArray);il.Emit(OpCodes.Ret);
        }
        foreach(var method in Types(assemblies["GodSeekerPlus"].MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody))
            foreach(var i in method.Body.Instructions)if(i.Operand is FieldReference f&&f.DeclaringType.FullName==pool.FullName&&f.Name==field.Name&&f.FieldType is ArrayType){
                if(i.OpCode!=OpCodes.Ldfld)throw new NotSupportedException("Unexpected pool array write");
                i.OpCode=OpCodes.Call;i.Operand=method.Module.ImportReference(getter);
            }
        var objectPool=game.GetType("ObjectPool");
        var create=objectPool.Methods.Single(m=>m.Name=="CreatePool"&&m.Parameters.Count==3&&!m.HasGenericParameters);
        foreach(var method in objectPool.Methods.Where(m=>m.HasBody))foreach(var i in method.Body.Instructions.ToArray())
            if(i.Operand is MethodReference call&&call.Name=="orig_CreatePool"&&call.Parameters.Count==2){
                method.Body.GetILProcessor().InsertBefore(i,Instruction.Create(OpCodes.Ldc_I4_0));i.Operand=create;
            }
        // Generated reflection wrappers for removed desktop fields are not used
        // by any of the bundled mods. Reject incoming uses before pruning them.
        var unused=Types(satchel.Types).Where(t=>t.Namespace=="Satchel.Reflected").ToHashSet();
        foreach(var assembly in assemblies.Values)foreach(var type in Types(assembly.MainModule.Types).Where(t=>!unused.Contains(t)&&!unused.Contains(t.DeclaringType)))
            foreach(var method in type.Methods.Where(m=>m.HasBody))foreach(var i in method.Body.Instructions)
                if(i.Operand is MemberReference member&&member.DeclaringType!=null&&member.DeclaringType.Namespace=="Satchel.Reflected")
                    throw new InvalidOperationException("Used desktop reflection wrapper: "+method.FullName+" -> "+member.FullName);
        foreach(var type in unused.Where(t=>t.DeclaringType==null))satchel.Types.Remove(type);
        var ui=assemblies["UnityEngine.UI"].MainModule.GetType("UnityEngine.UI.InputField");
        var endEdit=ui.NestedTypes.Single(t=>t.Name=="EndEditEvent");
        var addField=satchel.GetType("Satchel.BetterMenus.InputField").Methods.Single(m=>m.Name=="AddField");
        foreach(var local in addField.Body.Variables)if(local.VariableType.FullName==ui.FullName+"/SubmitEvent")local.VariableType=satchel.ImportReference(endEdit);
        foreach(var i in addField.Body.Instructions)if(i.Operand is MethodReference call){
            if(call.DeclaringType.FullName==ui.FullName+"/SubmitEvent"&&call.Name==".ctor")i.Operand=satchel.ImportReference(endEdit.Methods.Single(m=>m.IsConstructor));
            if(call.DeclaringType.FullName==ui.FullName&&call.Name=="set_onEndEdit")i.Operand=satchel.ImportReference(ui.Methods.Single(m=>m.Name=="set_onEndEdit"));
        }
        foreach(string name in new[]{"Modding.ModLoader","Modding.Preloader","Modding.Logger"}){
            var type=game.GetType(name);type.IsPublic=true;
            foreach(var m in type.Methods){m.IsPublic=true;}
            foreach(var f in type.Fields){f.IsPublic=true;}
        }
        game.GetType("Modding.ModHooks").Methods.Single(m=>m.Name=="LoadGlobalSettings").IsPublic=true;
        game.GetType("GameManager").Fields.Single(f=>f.Name=="timeSlowedCount").IsPublic=true;
        foreach(var name in new[]{"Assembly-CSharp","Satchel","GodSeekerPlus","DebugMod","QoL","Osmi"}){
            var assembly=assemblies[name];var module=assembly.MainModule;
            foreach(var t in module.GetTypeReferences())if(t.Scope.Name=="System.Private.CoreLib")t.Scope=module.TypeSystem.CoreLibrary;
            foreach(var r in module.AssemblyReferences.Where(r=>r.Name=="System.Private.CoreLib").ToArray())module.AssemblyReferences.Remove(r);
            string path=Path.Combine(stage,name+".dll");assembly.Write(path+".legacy");assembly.Dispose();File.Move(path+".legacy",path,true);
        }
        foreach(var assembly in assemblies.Where(p=>!new[]{"Assembly-CSharp","Satchel","GodSeekerPlus","DebugMod","QoL","Osmi"}.Contains(p.Key)))assembly.Value.Dispose();
        Console.WriteLine("Adapted current pool and input event ABI; exposed static native loader entry points");
    }
}
