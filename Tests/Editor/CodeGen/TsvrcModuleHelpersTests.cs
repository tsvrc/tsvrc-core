using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // TsvrcModule.AliasName/Deduplicate are `protected static`, so InternalsVisibleTo
    // alone doesn't reach them — this test-only subclass exposes them for direct testing.
    internal sealed class TsvrcModuleTestHarness : TsvrcModule
    {
        internal override void LoadConfig()
        {
        }

        internal static string CallAliasName(string goName) => AliasName(goName);

        internal static string CallDeduplicate(string baseName, HashSet<string> usedNames)
            => Deduplicate(baseName, usedNames);
    }

    public class TsvrcModuleHelpersTests
    {
        [Test]
        public void AliasName_WithWellFormedAlias_ExtractsInnerText()
        {
            Assert.AreEqual("Foo", TsvrcModuleTestHarness.CallAliasName("__Foo__"));
        }

        [Test]
        public void AliasName_WithNull_ReturnsNull()
        {
            Assert.IsNull(TsvrcModuleTestHarness.CallAliasName(null));
        }

        [Test]
        public void AliasName_NotStartingWithDoubleUnderscore_ReturnsNull()
        {
            Assert.IsNull(TsvrcModuleTestHarness.CallAliasName("Foo__"));
        }

        [Test]
        public void AliasName_NotEndingWithDoubleUnderscore_ReturnsNull()
        {
            Assert.IsNull(TsvrcModuleTestHarness.CallAliasName("__Foo"));
        }

        [Test]
        public void AliasName_ExactlyFourUnderscores_ReturnsNull()
        {
            // "____" starts and ends with "__" but Length == 4, failing the `> 4` guard.
            Assert.IsNull(TsvrcModuleTestHarness.CallAliasName("____"));
        }

        [Test]
        public void AliasName_FiveUnderscores_ExtractsSingleCharacter()
        {
            // Boundary one past the length-4 rejection: "_____" (5 chars) yields "_".
            Assert.AreEqual("_", TsvrcModuleTestHarness.CallAliasName("_____"));
        }

        [Test]
        public void AliasName_JustDoubleUnderscore_ReturnsNull()
        {
            // "__" trivially starts and ends with itself but is far too short (Length == 2).
            Assert.IsNull(TsvrcModuleTestHarness.CallAliasName("__"));
        }

        [Test]
        public void Deduplicate_NameNotInUsedSet_ReturnsNameUnchanged()
        {
            var used = new HashSet<string>();

            Assert.AreEqual("Foo", TsvrcModuleTestHarness.CallDeduplicate("Foo", used));
        }

        [Test]
        public void Deduplicate_SingleCollision_AppendsSuffixStartingAtTwo()
        {
            var used = new HashSet<string> { "Foo" };

            Assert.AreEqual("Foo2", TsvrcModuleTestHarness.CallDeduplicate("Foo", used));
        }

        [Test]
        public void Deduplicate_ChainedCollisions_IncrementsSuffixUntilFree()
        {
            var used = new HashSet<string> { "Foo", "Foo2", "Foo3" };

            Assert.AreEqual("Foo4", TsvrcModuleTestHarness.CallDeduplicate("Foo", used));
        }
    }
}
