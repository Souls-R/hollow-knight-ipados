using System;
using System.Collections.Generic;
using System.Reflection;

static class NativeHookRegression
{
    static int checks;
    static readonly List<string> trace=new List<string>();
    static bool nested;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); checks++; Console.WriteLine("PASS "+name); }
    static bool Terminal(On.HeroController.orig_CanAttack orig,HeroController self) {trace.Add("terminal"); return true;}
    static bool Twice(On.HeroController.orig_CanAttack orig,HeroController self) {trace.Add("twice"); return orig(self)&&orig(self);}
    static bool Nested(On.HeroController.orig_CanAttack orig,HeroController self)
    {
        if(nested) return orig(self);
        nested=true;
        try { return On.HeroController.NativeDispatch_CanAttack(self)&&orig(self); }
        finally {nested=false;}
    }
    static bool Failure(On.HeroController.orig_CanAttack orig,HeroController self) {throw new InvalidOperationException("expected");}
    static On.HeroController.orig_CanAttack retained;
    static bool Capture(On.HeroController.orig_CanAttack orig,HeroController self) {retained=orig;return false;}
    static System.Collections.IEnumerator DeferredMenu(On.UIManager.orig_ShowMenu orig,UIManager self,MenuScreen menu)
    {yield return orig(self,menu);}
    static int Main()
    {
        On.HeroController.CanAttack+=Terminal;
        On.HeroController.CanAttack+=Twice;
        Check(On.HeroController.NativeDispatch_CanAttack(null),"typed return value");
        Check(string.Join(",",trace)=="twice,terminal,terminal","orig may be called twice without skipping downstream hooks");
        trace.Clear(); On.HeroController.CanAttack-=Twice;
        On.HeroController.CanAttack+=Nested;
        Check(On.HeroController.NativeDispatch_CanAttack(null),"nested reentry succeeds");
        Check(trace.Count==2,"nested dispatch preserves outer continuation state");
        On.HeroController.CanAttack-=Nested;
        On.HeroController.CanAttack+=Failure;
        bool thrown=false;
        try {On.HeroController.NativeDispatch_CanAttack(null);} catch(InvalidOperationException){thrown=true;}
        Check(thrown,"hook exceptions propagate");
        On.HeroController.CanAttack-=Failure;
        trace.Clear();
        Check(On.HeroController.NativeDispatch_CanAttack(null)&&trace.Count==1,"continuation state restored after exception");
        On.HeroController.CanAttack+=Capture;
        Check(!On.HeroController.NativeDispatch_CanAttack(null),"a hook may retain orig without invoking it immediately");
        Check(retained(null),"retained orig remains callable after dispatch returns");
        On.HeroController.CanAttack-=Capture;
        On.HeroController.CanAttack-=Terminal;
        Check(retained(null),"retained orig keeps its downstream snapshot after unregistration");
        retained=null;
        bool originalReached=false;
        try {On.HeroController.NativeDispatch_CanAttack(null);} catch(Exception){originalReached=true;}
        Check(originalReached,"unhooked dispatch reaches preserved original body");
        int voidCalls=0; float observed=0;
        On.GameManager.hook_SetTimeScale_float terminal=(orig,self,value)=>{voidCalls++; observed=value;};
        On.GameManager.hook_SetTimeScale_float twice=(orig,self,value)=>{orig(self,value);orig(self,value);};
        On.GameManager.SetTimeScale_float+=terminal;
        On.GameManager.SetTimeScale_float+=twice;
        On.GameManager.NativeDispatch_SetTimeScale_float(null,.375f);
        Check(voidCalls==2 && observed==.375f,"void hooks preserve arguments and repeated orig");
        On.GameManager.SetTimeScale_float-=twice; On.GameManager.SetTimeScale_float-=terminal;
        int menuCalls=0;
        On.UIManager.hook_ShowMenu menuTerminal=(orig,self,menu)=>{menuCalls++;return new int[]{1}.GetEnumerator();};
        On.UIManager.ShowMenu+=menuTerminal;On.UIManager.ShowMenu+=DeferredMenu;
        var deferred=On.UIManager.NativeDispatch_ShowMenu(null,null);
        Check(menuCalls==0,"iterator hooks defer execution until coroutine advance");
        Check(deferred.MoveNext()&&deferred.Current is System.Collections.IEnumerator&&menuCalls==1,"iterator orig runs correctly on a later coroutine advance");
        On.UIManager.ShowMenu-=DeferredMenu;On.UIManager.ShowMenu-=menuTerminal;
        Console.WriteLine("NATIVE_HOOK_REGRESSION_PASS "+checks);
        return 0;
    }
}
