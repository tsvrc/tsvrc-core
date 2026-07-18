using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // CodeGen-specific complement to PrivateFieldAccess (Tests/TestUtil/): builds the
    // boxed module-entry structs and entry lists tests need to populate a module's
    // private `_entries`/`_poolEntries` field directly, exercising Wire()/
    // GenerateCode() without going through the real (project-coupled) LoadConfig()
    // pipeline.
    internal static class CodeGenModuleReflection
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
