using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    public class FactorySanitizeTests
    {
        // FactoryModule.Sanitize is `private static` — reflection is the only way in,
        // since InternalsVisibleTo only reaches internal (not private) members.
        private static string Sanitize(string raw)
        {
            MethodInfo method = typeof(FactoryModule).GetMethod("Sanitize", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "FactoryModule.Sanitize method signature changed or was removed.");
            return (string)method.Invoke(null, new object[] { raw });
        }

        [Test]
        public void Sanitize_NullInput_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, Sanitize(null));
        }

        [Test]
        public void Sanitize_EmptyString_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, Sanitize(""));
        }

        [Test]
        public void Sanitize_AllSymbols_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, Sanitize("!!!"));
        }

        [Test]
        public void Sanitize_LeadingDigit_PrependsUnderscore()
        {
            Assert.AreEqual("_123abc", Sanitize("123abc"));
        }

        [Test]
        public void Sanitize_NonAlphanumericSeparators_PascalCasesEachWord()
        {
            Assert.AreEqual("HelloWorld", Sanitize("hello world"));
            Assert.AreEqual("Group1Name", Sanitize("Group1-Name"));
        }

        [Test]
        public void Sanitize_AliasWrapper_UnwrapsBeforeProcessing()
        {
            Assert.AreEqual("Foo", Sanitize("__Foo__"));
            Assert.AreEqual("X", Sanitize("__x__"));
        }

        [Test]
        public void Sanitize_ExactlyFourUnderscores_TreatedAsLiteralNotAlias()
        {
            // "____" starts/ends with "__" but Length == 4 fails the `> 4` unwrap guard,
            // and underscore alone is not alphanumeric, so the result is empty.
            Assert.AreEqual(string.Empty, Sanitize("____"));
        }
    }
}
