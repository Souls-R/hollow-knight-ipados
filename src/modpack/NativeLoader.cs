using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Modding;
using UnityEngine;
using Logger=Modding.Logger;

namespace NativeModpack
{
    public static class Loader
    {
        public static object GetTimeSlowed(object owner) {return ((GameManager)owner).TimeSlowed;}
        public static void SetTimeSlowed(object owner,object value)
        {ref int count=ref GameManager.NativeTimeSlowedCountRef((GameManager)owner);count=Convert.ToBoolean(value)?Math.Max(1,count):0;}
        public static string AssemblyVersion(Assembly assembly)
        {var name=assembly.GetName();return (name.Name=="Satchel"?Satchel.AssemblyUtils.ver:name.Version.ToString())+"-native";}
        public static void WarmType(Type type)
        {
            foreach(FieldInfo field in type.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static))
                ReflectionHelper.GetFieldInfo(type,field.Name,!field.IsStatic);
        }
        static ref int SlowCount(GameManager owner) {return ref GameManager.NativeTimeSlowedCountRef(owner);}
        public static void RegisterAll()
        {
            ModHooks.LoadGlobalSettings();
            Logger.ClearOldModlogs();
            ReferenceFields.Register(typeof(GameManager).GetField("timeSlowedCount",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance),
                typeof(GameManager),typeof(int),new Vasi.Mirror.FuncByRef<GameManager,int>(SlowCount));
            Register(typeof(Satchel.Satchel),()=>new Satchel.Satchel());
            Register(typeof(Osmi.Osmi),()=>new Osmi.Osmi());
            Register(typeof(QoL.QoL),()=>new QoL.QoL());
            Register(typeof(DebugMod.DebugMod),()=>new DebugMod.DebugMod());
            Register(typeof(GodSeekerPlus.GodSeekerPlus),()=>new GodSeekerPlus.GodSeekerPlus());
            Register(typeof(AnyRadiance1.AnyRadiance1),()=>new AnyRadiance1.AnyRadiance1());
            var controls=new GameObject("NativeModpackControls");
            controls.AddComponent<ModControls>();
            UnityEngine.Object.DontDestroyOnLoad(controls);
        }
        static void Register(Type type,Func<Mod> factory)
        {
            try
            {
                Mod mod=factory();
                ModLoader.TryAddModInstance(type,new ModLoader.ModInstance{Mod=mod,Name=mod.GetName(),Enabled=false,Error=null});
                Logger.APILogger.Log("[NativeModpack] Constructed "+type.FullName);
            }
            catch(Exception error)
            {
                Logger.APILogger.LogError("[NativeModpack] "+type.FullName+": "+error);
                ModLoader.TryAddModInstance(type,new ModLoader.ModInstance{Name=type.Name,Error=ModLoader.ModErrorState.Construct});
            }
        }
        public static IEnumerator Preload(Preloader owner,
            Dictionary<string,List<(ModLoader.ModInstance,List<string>)>> requests,
            Dictionary<ModLoader.ModInstance,Dictionary<string,Dictionary<string,GameObject>>> objects,
            Dictionary<string,List<Func<IEnumerator>>> hooks)
        {
            if(requests.Count==0&&hooks.Count==0)
            {ModLoader.LoadState|=ModLoader.ModLoadState.Preloaded;yield break;}
            AudioListener.pause=true;
            try
            {
                if(requests.Count>0||hooks.Count>0)
                    yield return owner.DoPreloadScenes(requests,objects,hooks,"",1,0);
                yield return owner.CleanUpPreloading();
            }
            finally {AudioListener.pause=false;}
        }
    }
}
