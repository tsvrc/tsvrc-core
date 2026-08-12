using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Exercises the shared TsModule.Sanitize through TsModuleTestHarness.
    public class FactorySanitizeTests
    {
        private static string Sanitize(string raw) => TsModuleTestHarness.CallSanitize(raw);

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
        public void Sanitize_SurroundingUnderscores_TreatedAsSeparators()
        {
            Assert.AreEqual("Foo", Sanitize("__Foo__"));
            Assert.AreEqual("X", Sanitize("__x__"));
        }

        [Test]
        public void Sanitize_OnlyUnderscores_ReturnsEmptyString()
        {
            Assert.AreEqual(string.Empty, Sanitize("____"));
        }
    }
}
