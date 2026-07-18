using System.Reflection;
using NUnit.Framework;
using Tsvrc.Core;

namespace Tsvrc.Tests.EditMode
{
    public class WirePoolAttributeTests
    {
        private class DummyFieldHolder
        {
            [WirePool]
            public object NoDescription;

            [WirePool("used for enemy spawns")]
            public object WithDescription;
        }

        [Test]
        public void DefaultConstructor_LeavesDescriptionNull()
        {
            var attr = new WirePoolAttribute();

            Assert.IsNull(attr.Description);
        }

        [Test]
        public void ConstructorWithDescription_SetsDescription()
        {
            var attr = new WirePoolAttribute("used for enemy spawns");

            Assert.AreEqual("used for enemy spawns", attr.Description);
        }

        [Test]
        public void ReflectedFromField_WithNoDescription_RoundTripsAsNull()
        {
            FieldInfo field = typeof(DummyFieldHolder).GetField(nameof(DummyFieldHolder.NoDescription));
            var attr = field.GetCustomAttribute<WirePoolAttribute>();

            Assert.IsNotNull(attr);
            Assert.IsNull(attr.Description);
        }

        [Test]
        public void ReflectedFromField_WithDescription_RoundTripsDescription()
        {
            FieldInfo field = typeof(DummyFieldHolder).GetField(nameof(DummyFieldHolder.WithDescription));
            var attr = field.GetCustomAttribute<WirePoolAttribute>();

            Assert.IsNotNull(attr);
            Assert.AreEqual("used for enemy spawns", attr.Description);
        }
    }
}
