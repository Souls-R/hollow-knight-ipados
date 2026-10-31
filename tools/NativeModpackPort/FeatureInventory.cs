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
        using var game=AssemblyDefinition.ReadAssembly(Path.Combine(root,"native-stage/Assembly-CSharp.dll"));
        var startup=game.MainModule.GetType("StartManager");
        var installer=startup.Methods.Single(m=>m.Name=="NativeInstallLanguageHook");
        var field=installer.Body.Instructions.Select(i=>i.Operand).OfType<FieldReference>().Single();
        var constructor=installer.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().Single(m=>m.Name==".ctor");
        if(field.FieldType.FullName!=constructor.DeclaringType.FullName||field.Name!="LanguageGet")throw new InvalidOperationException("Startup language callback type mismatch");
        var entry=startup.Methods.Single(m=>m.Name=="Awake").Body.Instructions.First();
        if(entry.Operand is not MethodReference call||call.FullName!=installer.FullName)throw new InvalidOperationException("Startup must install a typed language callback before original Awake");
        Console.WriteLine("StartManager: exact native language delegate installed before game initialization");
        var gui=after.MainModule.GetType("DebugMod.GUIController");
        MethodReference[] Calls(MethodDefinition m)=>m.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().ToArray();
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        var update=Calls(gui.Methods.Single(m=>m.Name=="Update"));
        Require(update.Count(m=>m.Name=="SnapshotBindings")==1,"Debug binding iteration must use exactly one snapshot");
        Require(update.Any(m=>m.Name=="BindingOrNone")&&update.Any(m=>m.Name=="BeforeUpdate"),"Debug update must guard focus and missing master bindings");
        Require(!Types(after.MainModule.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(Calls).Any(m=>m.DeclaringType.FullName=="NativeModpack.LegacyInput"&&m.Name=="GetKeyDown"),"Debug shortcut calls must use the controller opt-in gate");
        Require(Calls(gui.Methods.Single(m=>m.Name=="BuildMenus")).Any(m=>m.Name=="ConfigureFonts"),"Debug must configure Chinese fonts before creating labels");
        var keyPanel=after.MainModule.GetType("DebugMod.KeyBindPanel");
        Require(Calls(keyPanel.Methods.Single(m=>m.Name=="UpdateHelpText")).Any(m=>m.Name=="PaginateHelp"),"Debug help must paginate before rendering");
        Require(Calls(keyPanel.Methods.Single(m=>m.Name=="RunBind")).Any(m=>m.Name=="RunHelpCommand"),"Debug help must support additional mod commands");
        Require(!after.MainModule.Types.Where(t=>t.Namespace=="DebugMod.Canvas").SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(Calls).Any(m=>m.Name=="DontDestroyOnLoad"),"Debug child objects must follow the persistent root");
        foreach(var (type,method) in new[]{("MenuButtonList","DoSelect"),("PromptMarker","Hide")})
        {var calls=Calls(game.MainModule.GetType(type).Methods.Single(m=>m.Name==method));Require(calls.Any(m=>m.Name=="StartWhenActive")&&!calls.Any(m=>m.Name=="StartCoroutine"),"Inactive UI coroutine guard missing: "+type);}
        Console.WriteLine("Debug UI: controller gate, snapshot, Chinese fonts, pagination and inactive menu guards verified");
    }
}
