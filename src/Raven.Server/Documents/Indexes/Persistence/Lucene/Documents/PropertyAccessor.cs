﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Jint.Native;
using Jint.Native.Object;
using Microsoft.CSharp.RuntimeBinder;
using Raven.Server.Documents.Patch;

namespace Raven.Server.Documents.Indexes.Persistence.Lucene.Documents
{
    public delegate object DynamicGetter(object target);

    public class PropertyAccessor : IPropertyAccessor
    {
        private readonly Dictionary<string, Accessor> Properties = new Dictionary<string, Accessor>();

        private readonly List<KeyValuePair<string, Accessor>> propertiesInOrder =
            new List<KeyValuePair<string, Accessor>>();

        public IEnumerable<(string Key, object Value, bool IsGroupByField)> GetPropertiesInOrder(object target)
        {
            foreach ((var key, var value) in propertiesInOrder)
            {
                yield return (key, value.GetValue(target), value.IsGroupByField);
            }
        }

        public static IPropertyAccessor Create(Type type)
        {
            if (type == typeof(ObjectInstance))
                return new JintPropertyAccessor(null);
            return new PropertyAccessor(type);
        }

        public object GetValue(string name, object target)
        {
            if (Properties.TryGetValue(name, out Accessor accessor))
                return accessor.GetValue(target);

            throw new InvalidOperationException(string.Format("The {0} property was not found", name));
        }

        private PropertyAccessor(Type type, HashSet<string> groupByFields = null)
        {
            var isValueType = type.GetTypeInfo().IsValueType;
            foreach (var prop in type.GetProperties())
            {
                var getMethod = isValueType
                    ? (Accessor)CreateGetMethodForValueType(prop, type)
                    : CreateGetMethodForClass(prop, type);

                if (groupByFields != null && groupByFields.Contains(prop.Name))
                    getMethod.IsGroupByField = true;

                Properties.Add(prop.Name, getMethod);
                propertiesInOrder.Add(new KeyValuePair<string, Accessor>(prop.Name, getMethod));
            }
        }

        private static ValueTypeAccessor CreateGetMethodForValueType(PropertyInfo prop, Type type)
        {
            var binder = Microsoft.CSharp.RuntimeBinder.Binder.GetMember(CSharpBinderFlags.None, prop.Name, type, new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) });
            return new ValueTypeAccessor(CallSite<Func<CallSite, object, object>>.Create(binder));
        }

        private static ClassAccessor CreateGetMethodForClass(PropertyInfo propertyInfo, Type type)
        {
            var getMethod = propertyInfo.GetGetMethod();

            if (getMethod == null)
                throw new InvalidOperationException(string.Format("Could not retrieve GetMethod for the {0} property of {1} type", propertyInfo.Name, type.FullName));

            var arguments = new[]
            {
                typeof (object)
            };

            var getterMethod = new DynamicMethod(string.Concat("_Get", propertyInfo.Name, "_"), typeof(object), arguments, propertyInfo.DeclaringType);
            var generator = getterMethod.GetILGenerator();

            generator.DeclareLocal(typeof(object));
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Castclass, propertyInfo.DeclaringType);
            generator.EmitCall(OpCodes.Callvirt, getMethod, null);

            if (propertyInfo.PropertyType.GetTypeInfo().IsClass == false)
                generator.Emit(OpCodes.Box, propertyInfo.PropertyType);

            generator.Emit(OpCodes.Ret);

            return new ClassAccessor((DynamicGetter)getterMethod.CreateDelegate(typeof(DynamicGetter)));
        }

        private class ValueTypeAccessor : Accessor
        {
            private readonly CallSite<Func<CallSite, object, object>> _callSite;

            public ValueTypeAccessor(CallSite<Func<CallSite, object, object>> callSite)
            {
                _callSite = callSite;
            }

            public override object GetValue(object target)
            {
                return _callSite.Target(_callSite, target);
            }
        }

        private class ClassAccessor : Accessor
        {
            private readonly DynamicGetter _dynamicGetter;

            public ClassAccessor(DynamicGetter dynamicGetter)
            {
                _dynamicGetter = dynamicGetter;
            }

            public override object GetValue(object target)
            {
                return _dynamicGetter(target);
            }
        }

        public abstract class Accessor
        {
            public abstract object GetValue(object target);

            public bool IsGroupByField;
        }

        internal static IPropertyAccessor CreateMapReduceOutputAccessor(Type type, HashSet<string> _groupByFields, bool isObjectInstance = false)
        {
            if (isObjectInstance || type == typeof(ObjectInstance))
                return new JintPropertyAccessor(_groupByFields);

            return new PropertyAccessor(type, _groupByFields);
        }

    }

    internal class JintPropertyAccessor : IPropertyAccessor
    {
        private HashSet<string> _groupByFields;

        public JintPropertyAccessor(HashSet<string> groupByFields)
        {
            _groupByFields = groupByFields;
        }

        public IEnumerable<(string Key, object Value, bool IsGroupByField)> GetPropertiesInOrder(object target)
        {
            var oi = target as ObjectInstance;
            if(oi == null)
                throw new ArgumentException($"JintPropertyAccessor.GetPropertiesInOrder is expecting a target of type ObjectInstance but got one of type {target.GetType().Name}.");
            foreach (var property in oi.GetOwnProperties())
            {
                yield return (property.Key, GetValue(property.Value.Value), _groupByFields?.Contains(property.Key)??false);
            }            
        }

        public object GetValue(string name, object target)
        {
            var oi = target as ObjectInstance;
            if (oi == null)
                throw new ArgumentException($"JintPropertyAccessor.GetValue is expecting a target of type ObjectInstance but got one of type {target.GetType().Name}.");
            if(oi.HasOwnProperty(name) == false)
                throw new MissingFieldException($"The target for 'JintPropertyAccessor.GetValue' doesn't contain the property {name}.");
            return GetValue(oi.GetProperty(name).Value);
        }

        private static object GetValue(JsValue jsValue)
        {
            if (jsValue.IsNull())
                return null;
            if (jsValue.IsString())
                return jsValue.AsString();
            if (jsValue.IsBoolean())
                return jsValue.AsBoolean(); 
            if (jsValue.IsNumber())
                return jsValue.AsNumber();
            if (jsValue.IsDate())
                return jsValue.AsDate();
            if (jsValue.IsObject())
            {
                return jsValue.AsObject();
            }
            if (jsValue.IsArray())
            {
                var arr = jsValue.AsArray();
                var array = new object[arr.GetLength()];
                var i = 0;
                foreach ((var key, var val) in arr.GetOwnProperties())
                {
                    if (key == "length")
                        continue;

                    array[i++] = GetValue(val.Value);
                }

                return array;
            }

            throw new NotSupportedException($"Was requested to extract the value out of a JsValue object but could not figure its type, value={jsValue}");
        }
    }
}
