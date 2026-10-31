using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NativeModpack
{
    // Replaces runtime IL emission with the same reflected field operations.
    public static class ObjectReflection
    {
        const BindingFlags InstanceFields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        public static object Clone(object source)
        {
            Type type=source.GetType();
            ConstructorInfo constructor=type.GetConstructor(InstanceFields,null,Type.EmptyTypes,null);
            if(constructor==null)throw new NotSupportedException("Only supports type with parameterless constructor");
            object result=constructor.Invoke(new object[0]);
            foreach(FieldInfo field in type.GetFields(InstanceFields))field.SetValue(result,field.GetValue(source));
            return result;
        }

        public static Action<Component> ChildReferences(Type type)
        {
            if(type.IsAbstract)throw new InvalidOperationException("Class "+type.FullName+" is abstract");
            if(!type.IsSubclassOf(typeof(Component)))throw new NotSupportedException("Class "+type.FullName+" is not Component");
            var fields=new List<FieldInfo>();
            var paths=new List<string>();
            var exceptions=new List<Exception>();
            foreach(FieldInfo field in type.GetFields(InstanceFields))
            {
                foreach(object attribute in field.GetCustomAttributes(false))
                {
                    Type attributeType=attribute.GetType();
                    if(attributeType.FullName!="Osmi.ChildRefs.ChildRefAttribute")continue;
                    string path=(string)attributeType.GetProperty("Path").GetValue(attribute,null);
                    Type valueType=field.FieldType;
                    if(valueType!=typeof(GameObject)&&!valueType.IsSubclassOf(typeof(Component)))
                        exceptions.Add(new InvalidOperationException("Type "+valueType.FullName+" is neither GameObject nor Component"));
                    else if(path.Length==0&&(valueType==typeof(GameObject)||valueType==typeof(Transform)))
                        exceptions.Add(new InvalidOperationException("Do not use Child Ref to get the "+valueType.Name+" of its own"));
                    else {fields.Add(field);paths.Add(path);}
                }
            }
            if(exceptions.Count==1)throw exceptions[0];
            if(exceptions.Count>1)throw new AggregateException(exceptions);
            return delegate(Component owner)
            {
                for(int i=0;i<fields.Count;i++)
                {
                    Type valueType=fields[i].FieldType;
                    Component child=paths[i].Length==0?owner:owner.transform.Find(paths[i]);
                    object value=valueType==typeof(GameObject)?(object)child.gameObject:
                        valueType==typeof(Transform)?child:child.GetComponent(valueType);
                    fields[i].SetValue(owner,value);
                }
            };
        }
    }
}
