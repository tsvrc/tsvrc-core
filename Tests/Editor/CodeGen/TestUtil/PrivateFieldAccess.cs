using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    // Centralized reflection helpers for poking private state on CodeGen module
    // instances (e.g. `_entries`, `_poolEntries`) and calling their private static/
    // instance methods directly. Every CodeGen module keeps its resolved config in a
    // private field never exposed publicly, so tests that want to exercise Wire()/
    // GenerateCode() without going through the real (project-coupled) LoadConfig()
    // pipeline need this.
    internal static class PrivateFieldAccess
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void SetField(object target, string fieldName, object value)
        {
            var field = FindField(target.GetType(), fieldName);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}. Signature changed?");
            field.SetValue(target, value);
        }

        internal static void SetField(Type staticType, string fieldName, object value)
        {
            var field = staticType.GetField(fieldName, StaticFlags);
            Assert.IsNotNull(field, $"Static field '{fieldName}' not found on {staticType.Name}. Signature changed?");
            field.SetValue(null, value);
        }

        internal static T GetField<T>(object target, string fieldName)
        {
            var field = FindField(target.GetType(), fieldName);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}. Signature changed?");
            return (T)field.GetValue(target);
        }

        internal static T GetField<T>(Type staticType, string fieldName)
        {
            var field = staticType.GetField(fieldName, StaticFlags);
            Assert.IsNotNull(field, $"Static field '{fieldName}' not found on {staticType.Name}. Signature changed?");
            return (T)field.GetValue(null);
        }

        internal static object InvokeStatic(Type type, string methodName, params object[] args)
        {
            var method = type.GetMethod(methodName, StaticFlags);
            Assert.IsNotNull(method, $"Method '{methodName}' not found on {type.Name}. Signature changed?");
            return method.Invoke(null, args);
        }

        internal static object InvokeInstance(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, InstanceFlags);
            Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}. Signature changed?");
            return method.Invoke(target, args);
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(name, InstanceFlags);
                if (field != null) return field;
            }
            return null;
        }

        // Builds a boxed instance of a module's private nested entry struct (e.g.
        // SingletonModule.SingletonEntry) with the given field values set, for feeding
        // GenerateCode()/Wire() tests without going through the real LoadConfig() pipeline.
        internal static object BuildEntry(Type structType, params (string field, object value)[] fields)
        {
            object instance = Activator.CreateInstance(structType);
            foreach (var (name, value) in fields)
            {
                var field = structType.GetField(name, InstanceFlags);
                Assert.IsNotNull(field, $"Field '{name}' not found on {structType.Name}. Signature changed?");
                field.SetValue(instance, value);
            }
            return instance;
        }

        // Builds a List<structType> (as a plain IList, boxed) containing the given boxed
        // entries - used to populate a module's private `List<TEntry> _entries` field.
        internal static IList BuildList(Type elementType, IEnumerable<object> entries)
        {
            var listType = typeof(List<>).MakeGenericType(elementType);
            var list = (IList)Activator.CreateInstance(listType);
            foreach (var entry in entries) list.Add(entry);
            return list;
        }

        internal static Type NestedType(Type owner, string nestedName)
        {
            var type = owner.GetNestedType(nestedName, BindingFlags.NonPublic);
            Assert.IsNotNull(type, $"Nested type '{nestedName}' not found on {owner.Name}. Signature changed?");
            return type;
        }
    }
}
