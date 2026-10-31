using System;
using System.Reflection;
using NativeModpack;
using MonoMod.Utils;

static class NativeModRuntimeRegression
{
    sealed class CallbackHolder {public static TeamCherry.Localization.Language.LanguageGetFunc LanguageGet=null;}
    sealed class Sample
    {
        int hidden=3;
        public readonly int fixedValue=9;
        public object child;
        private Sample() { }
        public static Sample Create() {return new Sample();}
        public int Value {get{return hidden;}}
    }
    sealed class NoDefault {public NoDefault(int value) { }}
    static int checks;
    static void Check(bool condition,string name)
    {if(!condition)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static int Main()
    {
        var languageField=typeof(CallbackHolder).GetField("LanguageGet");
        var setLanguage=(Action<Func<string,string,string>>)ReflectionBridge.GetStaticFieldSetter<Func<string,string,string>>(languageField);
        int languageCalls=0;
        Func<string,string,string> callbacks=(key,sheet)=>{languageCalls++;return "first";};
        callbacks+=(key,sheet)=>{languageCalls++;return key+"/"+sheet;};
        setLanguage(callbacks);
        Check(CallbackHolder.LanguageGet("title","menu")=="title/menu"&&languageCalls==2,"language callback adapts Func to the game's delegate and preserves multicast order");
        var getLanguage=(Func<Func<string,string,string>>)ReflectionBridge.GetStaticFieldGetter<Func<string,string,string>>(languageField);
        Check(getLanguage()("start","menu")=="start/menu","delegate fields can be read through a compatible Func signature");
        setLanguage(null);Check(CallbackHolder.LanguageGet==null,"language callback can be cleared");
        bool incompatibleRejected=false;
        try {((Action<Action>)ReflectionBridge.GetStaticFieldSetter<Action>(languageField))(()=>{});}catch(ArgumentException){incompatibleRejected=true;}
        Check(incompatibleRejected,"incompatible delegate signatures are rejected");
        int debugCommands=0;
        foreach(var command in typeof(DebugMod.BindableFunctions).GetMethods(BindingFlags.Static|BindingFlags.Public))
            if(Attribute.IsDefined(command,typeof(DebugMod.BindableMethod)))
            {debugCommands++;if(command.GetParameters().Length!=0)throw new Exception("Command requires arguments: "+command.Name);}
        Check(debugCommands==132,"all 132 supplied Debug commands remain available");
        int qolModules=0,godModules=0;
        foreach(var type in typeof(QoL.QoL).Assembly.GetTypes())if(type.IsSubclassOf(typeof(QoL.Modules.FauxMod))&&!type.IsAbstract)qolModules++;
        foreach(var type in typeof(GodSeekerPlus.GodSeekerPlus).Assembly.GetTypes())
            if(!type.IsAbstract)for(var parent=type.BaseType;parent!=null;parent=parent.BaseType)
                if(parent.FullName=="GodSeekerPlus.Modules.Module"){godModules++;break;}
        Check(qolModules==16,"all 16 supplied QoL modules are retained");
        Check(godModules==51,"all 51 supplied GodSeekerPlus modules are retained (found "+godModules+")");
        string key="test.shared";
        var manual=new PatchLease(key,true);
        Check(!PatchFlags.Enabled(key),"manual native patch starts disabled");
        manual.Apply();manual.Apply();
        Check(PatchFlags.Enabled(key),"repeated Apply is idempotent");
        var second=new PatchLease(key,false);manual.Dispose();
        Check(PatchFlags.Enabled(key),"shared native patch stays active while another lease exists");
        second.Undo();second.Dispose();
        Check(!PatchFlags.Enabled(key),"Undo and Dispose cannot over-release a patch");

        var method=typeof(QoL.Modules.FastMenus).GetMethod("DecreaseWait",BindingFlags.Static|BindingFlags.NonPublic);
        var callback=(MonoMod.Cil.ILContext.Manipulator)Delegate.CreateDelegate(typeof(MonoMod.Cil.ILContext.Manipulator),method);
        PatchRegistrations.Add("test.registration",null,callback);PatchRegistrations.Add("test.registration",null,callback);
        PatchRegistrations.Remove("test.registration",callback);
        Check(PatchFlags.Enabled("QoL.FastMenus"),"duplicate registrations keep independent leases");
        PatchRegistrations.Remove("test.registration",callback);PatchRegistrations.Remove("test.registration",callback);
        Check(!PatchFlags.Enabled("QoL.FastMenus"),"last unregistration removes only its compiled patch");

        var sample=Sample.Create();sample.child=new object();
        var field=typeof(Sample).GetField("hidden",BindingFlags.Instance|BindingFlags.NonPublic);
        var get=(Func<Sample,int>)ReflectionBridge.GetInstanceFieldGetter<Sample,int>(field);
        var set=(Action<Sample,int>)ReflectionBridge.GetInstanceFieldSetter<Sample,int>(field);
        set(sample,41);
        Check(get(sample)==41&&sample.Value==41,"native reflection reads and writes private value fields");
        Check(ReferenceEquals(get,ReflectionBridge.GetInstanceFieldGetter<Sample,int>(field)),"typed accessors are cached");
        bool readonlyRejected=false;
        try{ReflectionBridge.GetInstanceFieldSetter<Sample,int>(typeof(Sample).GetField("fixedValue"));}catch(ArgumentException){readonlyRejected=true;}
        Check(readonlyRejected,"readonly setters are rejected explicitly");
        var clone=(Sample)ObjectReflection.Clone(sample);
        Check(!ReferenceEquals(sample,clone)&&clone.Value==41&&ReferenceEquals(sample.child,clone.child),"Osmi clone preserves constructor and shallow field semantics");
        bool constructorRejected=false;
        try{ObjectReflection.Clone(new NoDefault(1));}catch(NotSupportedException){constructorRejected=true;}
        Check(constructorRejected,"Osmi clone rejects types without a parameterless constructor");
        var data=new DynamicData(sample);
        Check(data.Get<object>("sharedHealthManager")==null,"unassigned attached health state returns null");
        var healthState=new object();data.Set("sharedHealthManager",healthState);
        Check(ReferenceEquals(new DynamicData(sample).Get<object>("sharedHealthManager"),healthState),"attached state is shared across wrappers for the same enemy");
        Check(new DynamicData(clone).Get<object>("sharedHealthManager")==null,"attached health state is isolated between enemies");
        data.Set("sharedHealthManager",null);
        Check(data.Get<object>("sharedHealthManager")==null,"detaching shared health clears its attached state");
        data.Set("hidden",52);
        Check(sample.Value==52,"real private fields retain precedence over attached state");

        var text=new System.Runtime.CompilerServices.DefaultInterpolatedStringHandler(0,2,System.Globalization.CultureInfo.InvariantCulture);
        text.AppendFormatted(1.25,7,"F2");text.AppendLiteral("/");text.AppendFormatted("x",-3,null);
        Check(text.ToStringAndClear()=="   1.25/x  ","native API formatting preserves alignment and format strings");
        Check(text.ToString()=="","formatting clear releases previous content");
        Console.WriteLine("NATIVE_MOD_RUNTIME_REGRESSION_PASS "+checks);return 0;
    }
}
