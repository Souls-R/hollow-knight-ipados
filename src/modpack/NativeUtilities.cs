using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MonoMod.Cil
{
    // Manipulator identity is retained only for registration. Its original
    // method body is processed at build time and never executes on the device.
    public sealed class ILContext {public delegate void Manipulator(ILContext context);}
}
namespace MonoMod.Utils
{
    public delegate object FastReflectionDelegate(object instance,object[] arguments);
    public static class FastReflectionHelper
    {
        public delegate object FastInvoker(object instance,object[] arguments);
        public static FastReflectionDelegate GetFastDelegate(MethodInfo method,bool directBoxValueAccess=true)
        {return (instance,args)=>method.Invoke(instance,args);}
        public static FastInvoker GetFastInvoker(MethodBase method)
        {return (instance,args)=>method.Invoke(instance,args);}
        public static FastInvoker GetFastInvoker(MethodBase method,bool directBoxValueAccess)
        {return (instance,args)=>method.Invoke(instance,args);}
    }
    public static class ReflectionBridge
    {
        static readonly Dictionary<Tuple<MemberInfo,Type,Type,int>,Delegate> cache=new Dictionary<Tuple<MemberInfo,Type,Type,int>,Delegate>();
        static Delegate Cached(MemberInfo member,Type owner,Type value,int kind,Func<Delegate> create)
        {
            var key=Tuple.Create(member,owner,value,kind);
            lock(cache) {Delegate found;if(cache.TryGetValue(key,out found))return found;return cache[key]=create();}
        }
        public static Delegate GetInstanceFieldGetter<TType,TField>(FieldInfo field)
        {return Cached(field,typeof(TType),typeof(TField),0,()=>new Func<TType,TField>(obj=>(TField)field.GetValue(obj)));}
        public static Delegate GetStaticFieldGetter<TField>(FieldInfo field)
        {return Cached(field,typeof(object),typeof(TField),1,()=>new Func<TField>(()=>(TField)field.GetValue(null)));}
        public static Delegate GetInstanceFieldSetter<TType,TField>(FieldInfo field)
        {
            if(field.IsLiteral||field.IsInitOnly)throw new ArgumentException("Field cannot be readonly or const");
            return Cached(field,typeof(TType),typeof(TField),2,()=>new Action<TType,TField>((obj,value)=>field.SetValue(obj,value)));
        }
        public static Delegate GetStaticFieldSetter<TField>(FieldInfo field)
        {
            if(field.IsLiteral||field.IsInitOnly)throw new ArgumentException("Field cannot be readonly or const");
            return Cached(field,typeof(object),typeof(TField),3,()=>new Action<TField>(value=>field.SetValue(null,value)));
        }
        public static Delegate GetInstancePropertyGetter<TType,TValue>(PropertyInfo property)
        {return Cached(property,typeof(TType),typeof(TValue),4,()=>new Func<TType,TValue>(obj=>(TValue)property.GetValue(obj,null)));}
        public static Delegate GetStaticPropertyGetter<TValue>(PropertyInfo property)
        {return Cached(property,typeof(object),typeof(TValue),5,()=>new Func<TValue>(()=>(TValue)property.GetValue(null,null)));}
        public static Delegate GetInstancePropertySetter<TType,TValue>(PropertyInfo property)
        {return Cached(property,typeof(TType),typeof(TValue),6,()=>new Action<TType,TValue>((obj,value)=>property.SetValue(obj,value,null)));}
        public static Delegate GetStaticPropertySetter<TValue>(PropertyInfo property)
        {return Cached(property,typeof(object),typeof(TValue),7,()=>new Action<TValue>(value=>property.SetValue(null,value,null)));}
    }
    public static class Extensions
    {
        public static MethodInfo GetStateMachineTarget(this MethodInfo method)
        {
            var attr=(IteratorStateMachineAttribute)Attribute.GetCustomAttribute(method,typeof(IteratorStateMachineAttribute));
            if(attr==null)throw new MissingMemberException("Iterator state machine: "+method);
            return attr.StateMachineType.GetMethod("MoveNext",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        }
        public static FastReflectionHelper.FastInvoker GetFastInvoker(this MethodBase method,bool directBoxValueAccess=true)
        {return FastReflectionHelper.GetFastInvoker(method,directBoxValueAccess);}
    }
    public sealed class DynamicData
    {
        static readonly ConditionalWeakTable<object,Dictionary<string,object>> attached=new ConditionalWeakTable<object,Dictionary<string,object>>();
        readonly object owner;
        readonly Type type;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        public DynamicData(object value){owner=value;type=value is Type?(Type)value:value.GetType();}
        FieldInfo Field(string name)
        {
            for(Type current=type;current!=null;current=current.BaseType)
            {var field=current.GetField(name,flags|BindingFlags.DeclaredOnly);if(field!=null)return field;}
            return null;
        }
        public T Get<T>(string name)
        {
            var field=Field(name);
            if(field!=null)return (T)field.GetValue(owner is Type?null:owner);
            var property=type.GetProperty(name,flags);
            if(property!=null)return (T)property.GetValue(owner is Type?null:owner,null);
            var values=attached.GetOrCreateValue(owner);object value;
            lock(values) {return values.TryGetValue(name,out value)&&value!=null?(T)value:default(T);}
        }
        public void Set(string name,object value)
        {
            var field=Field(name);
            if(field!=null){field.SetValue(owner is Type?null:owner,value);return;}
            var property=type.GetProperty(name,flags);
            if(property!=null){property.SetValue(owner is Type?null:owner,value,null);return;}
            var values=attached.GetOrCreateValue(owner);
            lock(values)values[name]=value;
        }
    }
}
namespace MonoMod.ModInterop
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class ModExportNameAttribute:Attribute
    {public string Name;public ModExportNameAttribute(string name){Name=name;}}
    [AttributeUsage(AttributeTargets.Class|AttributeTargets.Field)] public sealed class ModImportNameAttribute:Attribute
    {public string Name;public ModImportNameAttribute(string name){Name=name;}}
    public static class ModInteropManager
    {
        static readonly List<Type> registered=new List<Type>();
        static readonly Dictionary<string,MethodInfo> exports=new Dictionary<string,MethodInfo>(StringComparer.Ordinal);
        public static void ModInterop(this Type type)
        {
            if(!registered.Contains(type))registered.Add(type);
            var export=(ModExportNameAttribute)Attribute.GetCustomAttribute(type,typeof(ModExportNameAttribute));
            if(export!=null)foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.Static))
                exports[export.Name+"."+method.Name]=method;
            foreach(var consumer in registered)
            {
                var group=(ModImportNameAttribute)Attribute.GetCustomAttribute(consumer,typeof(ModImportNameAttribute));
                foreach(var field in consumer.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static))
                {
                    if(!typeof(Delegate).IsAssignableFrom(field.FieldType))continue;
                    var item=(ModImportNameAttribute)Attribute.GetCustomAttribute(field,typeof(ModImportNameAttribute));
                    string key=item!=null?item.Name:(group==null?null:group.Name+"."+field.Name);
                    MethodInfo method;
                    if(key!=null&&exports.TryGetValue(key,out method))field.SetValue(null,Delegate.CreateDelegate(field.FieldType,method));
                }
            }
        }
    }
}
