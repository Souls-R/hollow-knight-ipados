using System;
using System.Reflection;
using MonoMod.Cil;
using NativeModpack;

// Source-compatible registration handles. Every accepted handle selects an
// already-compiled method variant; unknown requests fail explicitly.
namespace MonoMod.RuntimeDetour
{
    public struct ILHookConfig {public bool ManualApply;}
    public struct DetourConfig {public bool ManualApply;}
    public sealed class ILHook:IDisposable
    {
        readonly PatchLease lease;
        public bool IsApplied {get{return lease.IsApplied;}}
        public ILHook(MethodBase target,ILContext.Manipulator callback):this(target,callback,new ILHookConfig()){}
        public ILHook(MethodBase target,ILContext.Manipulator callback,ILHookConfig config)
        {lease=new PatchLease(PatchFlags.Identify(target,callback),config.ManualApply);}
        public void Apply(){lease.Apply();}
        public void Undo(){lease.Undo();}
        public void Dispose(){lease.Dispose();}
    }
    public sealed class Detour:IDisposable
    {
        readonly PatchLease lease;
        public Detour(MethodBase target,MethodBase replacement,DetourConfig config)
        {
            if(target.DeclaringType.Name!="BossSceneController" || target.Name!="get_BossLevel" || replacement.DeclaringType.FullName!="GodSeekerPlus.Modules.BossChallenge.P5Health" || replacement.Name!="OverrideLevel")
                throw new NotSupportedException("No native detour variant for "+target+" -> "+replacement);
            lease=new PatchLease("GodSeekerPlus.P5Health",config.ManualApply);
        }
        public void Apply(){lease.Apply();}
        public void Undo(){lease.Undo();}
        public void Dispose(){lease.Dispose();}
    }
}
namespace MonoMod.RuntimeDetour.HookGen
{
    public static class HookEndpointManager
    {
        public static void Modify(MethodBase method,ILContext.Manipulator callback)
        {PatchRegistrations.Add(method.ToString(),method,callback);}
        public static void Unmodify(MethodBase method,ILContext.Manipulator callback)
        {PatchRegistrations.Remove(method.ToString(),callback);}
        public static void Modify(MethodBase method,Delegate callback)
        {PatchRegistrations.Add(method.ToString(),method,callback);}
        public static void Unmodify(MethodBase method,Delegate callback)
        {PatchRegistrations.Remove(method.ToString(),callback);}
        public static void Modify<T>(MethodBase method,ILContext.Manipulator callback){Modify(method,callback);}
        public static void Unmodify<T>(MethodBase method,ILContext.Manipulator callback){Unmodify(method,callback);}
    }
}
