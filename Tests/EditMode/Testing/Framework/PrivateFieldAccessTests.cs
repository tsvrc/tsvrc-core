using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode.Testing.Framework
{
    // PrivateFieldAccess has no Unity/Tsvrc.Runtime dependency (see its own doc comment), so
    // every case here uses plain POCOs rather than UdonSharpBehaviours - proving the contract
    // holds for exactly the "any object or static type" claim it makes.
    public class PrivateFieldAccessTests
    {
        private class Base
        {
            // Reached only reflectively, via string names, from the tests below - never by
            // ordinary C# call sites. Flagged "unused" by static analysis; keep it anyway.
            private int _baseField = 1;
            private static int _baseStaticField = 10;

            private int BasePrivateMethod(int x) => x + 1;
            private static int BaseStaticMethod(int x) => x * 2;
        }

        private class Derived : Base
        {
            // Same as Base's members above: reflection-only, flagged "unused"/"make readonly"
            // by static analysis, and deliberately not readonly since SetField mutates it.
            private string _derivedField = "hello";
        }

        [Test]
        public void GetField_FieldOnExactType_ReturnsValue()
        {
            var target = new Derived();

            Assert.AreEqual("hello", PrivateFieldAccess.GetField<string>(target, "_derivedField"));
        }

        [Test]
        public void GetField_FieldDeclaredOnBaseType_ReturnsValue()
        {
            // FindField walks BaseType explicitly - GetField(name) on the derived Type alone
            // would not surface a private member declared only on the base class.
            var target = new Derived();

            Assert.AreEqual(1, PrivateFieldAccess.GetField<int>(target, "_baseField"));
        }

        [Test]
        public void SetField_Instance_FieldDeclaredOnBaseType_MutatesInPlace()
        {
            var target = new Derived();

            PrivateFieldAccess.SetField(target, "_baseField", 42);

            Assert.AreEqual(42, PrivateFieldAccess.GetField<int>(target, "_baseField"));
        }

        [Test]
        public void GetField_UnknownFieldName_ThrowsAssertionException()
        {
            var target = new Derived();

            Assert.Throws<AssertionException>(() => PrivateFieldAccess.GetField<int>(target, "_doesNotExist"));
        }

        [Test]
        public void SetField_Static_MutatesTypeState()
        {
            PrivateFieldAccess.SetField(typeof(Base), "_baseStaticField", 99);

            Assert.AreEqual(99, PrivateFieldAccess.GetField<int>(typeof(Base), "_baseStaticField"));

            PrivateFieldAccess.SetField(typeof(Base), "_baseStaticField", 10); // restore
        }

        [Test]
        public void GetField_UnknownStaticFieldName_ThrowsAssertionException()
        {
            Assert.Throws<AssertionException>(() => PrivateFieldAccess.GetField<int>(typeof(Base), "_doesNotExist"));
        }

        [Test]
        public void InvokeInstance_MethodDeclaredOnBaseType_InvokesThroughDerivedInstance()
        {
            // Mirrors the class's own documented nuance: Type.GetMethod does not surface a
            // private base-class method through a derived Type without walking the hierarchy.
            var target = new Derived();

            object result = PrivateFieldAccess.InvokeInstance(target, "BasePrivateMethod", 5);

            Assert.AreEqual(6, result);
        }

        [Test]
        public void InvokeInstance_UnknownMethodName_ThrowsAssertionException()
        {
            var target = new Derived();

            Assert.Throws<AssertionException>(() => PrivateFieldAccess.InvokeInstance(target, "DoesNotExist"));
        }

        [Test]
        public void InvokeStatic_PrivateStaticMethod_ReturnsResult()
        {
            object result = PrivateFieldAccess.InvokeStatic(typeof(Base), "BaseStaticMethod", 5);

            Assert.AreEqual(10, result);
        }

        [Test]
        public void InvokeStatic_UnknownMethodName_ThrowsAssertionException()
        {
            Assert.Throws<AssertionException>(() => PrivateFieldAccess.InvokeStatic(typeof(Base), "DoesNotExist"));
        }
    }
}
