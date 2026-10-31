using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NativeModpack
{
    public static class PatchFlags
    {
        static readonly Dictionary<string,int> counts=new Dictionary<string,int>();
        public static bool Enabled(string key) { int value; return counts.TryGetValue(key,out value) && value>0; }
        public static void Acquire(string key) { int value; counts.TryGetValue(key,out value); counts[key]=value+1; }
        public static void Release(string key) { int value; if(counts.TryGetValue(key,out value) && value>0) counts[key]=value-1; }
        public static string Identify(MethodBase target,Delegate callback)
        {
            string type=callback.Method.DeclaringType.FullName, method=callback.Method.Name;
            if(type.StartsWith("Osmi.OsmiHooks")) return target!=null && target.DeclaringType.Name=="HealthManager"?"Osmi.EnemyHitEvents":"Osmi.PauseEvents";
            if(type.StartsWith("DebugMod.MonoBehaviours.TimeScale")) return "DebugMod.TimeScale";
            foreach(string module in new[]{"FastMenus","FasterLoads","LockMenuCursor","OldDashmaster","PatchedBosses","SpeedBroke","UnencryptedSaves"})
                if(type=="QoL.Modules."+module) return "QoL."+module;
            if(type=="GodSeekerPlus.Modules.Cosmetic.NoLowHealthEffect") return "GodSeekerPlus.NoLowHealthEffect";
            if(type=="GodSeekerPlus.Modules.QoL.DoorDefaultBegin") return "GodSeekerPlus.DoorDefaultBegin";
            throw new NotSupportedException("No precompiled native patch for "+type+"."+method+" -> "+target);
        }
    }
    public sealed class PatchLease:IDisposable
    {
        readonly string key;
        bool applied;
        public bool IsApplied {get{return applied;}}
        public PatchLease(string value,bool manual) {key=value;if(!manual)Apply();}
        public void Apply() {if(applied)return;PatchFlags.Acquire(key);applied=true;}
        public void Undo() {if(!applied)return;PatchFlags.Release(key);applied=false;}
        public void Dispose() {Undo();}
    }
    public static class PatchRegistrations
    {
        static readonly Dictionary<string,Dictionary<Delegate,Stack<PatchLease>>> hooks=new Dictionary<string,Dictionary<Delegate,Stack<PatchLease>>>();
        public static void Add(string boundary,MethodBase target,Delegate callback)
        {
            string key=PatchFlags.Identify(target,callback);
            Dictionary<Delegate,Stack<PatchLease>> group;
            if(!hooks.TryGetValue(boundary,out group)) hooks[boundary]=group=new Dictionary<Delegate,Stack<PatchLease>>();
            Stack<PatchLease> values;
            if(!group.TryGetValue(callback,out values)) group[callback]=values=new Stack<PatchLease>();
            values.Push(new PatchLease(key,false));
        }
        public static void Remove(string boundary,Delegate callback)
        {
            Dictionary<Delegate,Stack<PatchLease>> group; Stack<PatchLease> values;
            if(!hooks.TryGetValue(boundary,out group)||!group.TryGetValue(callback,out values)||values.Count==0)return;
            values.Pop().Dispose();
            if(values.Count==0)group.Remove(callback);
        }
    }
    public static class Paths
    {
        public static string AssemblyLocation(Assembly assembly)
        {
            string name=assembly.GetName().Name;
            string directory=System.IO.Path.Combine(Application.persistentDataPath,"HollowKnightMods","NativeModpack",name);
            System.IO.Directory.CreateDirectory(directory);
            // A stable profile location, independent of the signed app path.
            // The filename is an identity only; no DLL is loaded from this path.
            return System.IO.Path.Combine(directory,name+".dll");
        }
    }
    public static class ReferenceFields
    {
        static readonly Dictionary<string,Delegate> getters=new Dictionary<string,Delegate>();
        static string Key(FieldInfo field,Type owner,Type value)
        {return field.DeclaringType.Assembly.GetName().Name+"|"+field.DeclaringType.FullName+"|"+field.Name+"|"+owner.FullName+"|"+value.FullName;}
        public static void Register(FieldInfo field,Type owner,Type value,Delegate getter)
        {getters[Key(field,owner,value)]=getter;}
        public static Delegate Get<TOwner,TValue>(FieldInfo field)
        {
            Delegate getter;
            if(getters.TryGetValue(Key(field,typeof(TOwner),typeof(TValue)),out getter))return getter;
            throw new NotSupportedException("Field reference was not compiled: "+field.DeclaringType.FullName+"."+field.Name);
        }
        public static Delegate GetStatic<TValue>(FieldInfo field)
        {
            Delegate getter;
            if(getters.TryGetValue(Key(field,field.DeclaringType,typeof(TValue)),out getter))return getter;
            throw new NotSupportedException("Static field reference was not compiled: "+field.DeclaringType.FullName+"."+field.Name);
        }
    }
}
