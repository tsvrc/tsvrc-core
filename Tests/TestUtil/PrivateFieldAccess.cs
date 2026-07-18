using System;
using System.Reflection;
using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Shared reflection helpers for poking private fields/methods on production
    // instances and static types from tests, across every test assembly (Edit Mode,
    // Play Mode, and the CodeGen suite's own module-entry helpers build on top of
    // this in Tests/Editor/CodeGen/TestUtil/CodeGenModuleReflection.cs).
    public static class PrivateFieldAccess
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static void SetField(object target, string fieldName, object value)
        {
            var field = FindField(target.GetType(), fieldName);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}. Signature changed?");
            field.SetValue(target, value);
        }

        public static void SetField(Type staticType, string fieldName, object value)
        {
            var field = staticType.GetField(fieldName, StaticFlags);
            Assert.IsNotNull(field, $"Static field '{fieldName}' not found on {staticType.Name}. Signature changed?");
            field.SetValue(null, value);
        }

        public static T GetField<T>(object target, string fieldName)
        {
            var field = FindField(target.GetType(), fieldName);
            Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}. Signature changed?");
            return (T)field.GetValue(target);
        }

        public static T GetField<T>(Type staticType, string fieldName)
        {
            var field = staticType.GetField(fieldName, StaticFlags);
            Assert.IsNotNull(field, $"Static field '{fieldName}' not found on {staticType.Name}. Signature changed?");
            return (T)field.GetValue(null);
        }

        public static object InvokeStatic(Type type, string methodName, params object[] args)
        {
            var method = type.GetMethod(methodName, StaticFlags);
            Assert.IsNotNull(method, $"Method '{methodName}' not found on {type.Name}. Signature changed?");
            return method.Invoke(null, args);
        }

        public static object InvokeInstance(object target, string methodName, params object[] args)
        {
            var method = FindMethod(target.GetType(), methodName);
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

        // Type.GetMethod(name, bindingFlags) does not surface a *private* method
        // declared only on a base class when called on a derived type — private
        // members are only reflectively visible via GetMethod on the exact type that
        // declares them. Walk the hierarchy explicitly, same as FindField above, so
        // InvokeInstance works through test-double subclasses of a production type
        // (e.g. calling a private TsProcess method through a TsProcessTestSubclass
        // instance).
        private static MethodInfo FindMethod(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var method = t.GetMethod(name, InstanceFlags);
                if (method != null) return method;
            }
            return null;
        }
    }
}
