using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // TsModule.AliasName/Deduplicate are `protected static`, so InternalsVisibleTo
    // alone doesn't reach them — this test-only subclass exposes them for direct testing.
    internal sealed class TsModuleTestHarness : TsModule
    {
        internal override void LoadConfig()
        {
        }

        internal static string CallAliasName(string goName) => AliasName(goName);

        internal static string CallDeduplicate(string baseName, HashSet<string> usedNames)
            => Deduplicate(baseName, usedNames);

        internal static bool CallIsTsBehaviourType(string shortName, string ns)
            => IsTsBehaviourType(shortName, ns);
    }

    public class TsModuleHelpersTests
    {
        [Test]
        public void AliasName_WithWellFormedAlias_ExtractsInnerText()
        {
            Assert.AreEqual("Foo", TsModuleTestHarness.CallAliasName("__Foo__"));
        }

        [Test]
        public void AliasName_WithNull_ReturnsNull()
        {
            Assert.IsNull(TsModuleTestHarness.CallAliasName(null));
        }

        [Test]
        public void AliasName_NotStartingWithDoubleUnderscore_ReturnsNull()
        {
            Assert.IsNull(TsModuleTestHarness.CallAliasName("Foo__"));
        }

        [Test]
        public void AliasName_NotEndingWithDoubleUnderscore_ReturnsNull()
        {
            Assert.IsNull(TsModuleTestHarness.CallAliasName("__Foo"));
        }

        [Test]
        public void AliasName_ExactlyFourUnderscores_ReturnsNull()
        {
            // "____" starts and ends with "__" but Length == 4, failing the `> 4` guard.
            Assert.IsNull(TsModuleTestHarness.CallAliasName("____"));
        }

        [Test]
        public void AliasName_FiveUnderscores_ExtractsSingleCharacter()
        {
            // Boundary one past the length-4 rejection: "_____" (5 chars) yields "_".
            Assert.AreEqual("_", TsModuleTestHarness.CallAliasName("_____"));
        }

        [Test]
        public void AliasName_JustDoubleUnderscore_ReturnsNull()
        {
            // "__" trivially starts and ends with itself but is far too short (Length == 2).
            Assert.IsNull(TsModuleTestHarness.CallAliasName("__"));
        }

        [Test]
        public void Deduplicate_NameNotInUsedSet_ReturnsNameUnchanged()
        {
            var used = new HashSet<string>();

            Assert.AreEqual("Foo", TsModuleTestHarness.CallDeduplicate("Foo", used));
        }

        [Test]
        public void Deduplicate_SingleCollision_AppendsSuffixStartingAtTwo()
        {
            var used = new HashSet<string> { "Foo" };

            Assert.AreEqual("Foo2", TsModuleTestHarness.CallDeduplicate("Foo", used));
        }

        [Test]
        public void Deduplicate_ChainedCollisions_IncrementsSuffixUntilFree()
        {
            var used = new HashSet<string> { "Foo", "Foo2", "Foo3" };

            Assert.AreEqual("Foo4", TsModuleTestHarness.CallDeduplicate("Foo", used));
        }

        [Test]
        public void IsTsBehaviourType_RealTsBehaviourSubclass_ReturnsTrue()
        {
            // Tsvrc.StateMachine.StateManager : TsvrcBehaviour - a real production type,
            // not a test double, so this exercises the actual inheritance chain in this project.
            Assert.IsTrue(TsModuleTestHarness.CallIsTsBehaviourType("StateManager", "Tsvrc.StateMachine"));
        }

        [Test]
        public void IsTsBehaviourType_UdonSharpBehaviourNotExtendingTsBehaviour_ReturnsFalse()
        {
            // TsRoot : UdonSharpBehaviour directly - never TsvrcBehaviour.
            Assert.IsFalse(TsModuleTestHarness.CallIsTsBehaviourType("TsRoot", "Tsvrc.Core.Generated"));
        }

        [Test]
        public void IsTsBehaviourType_TypeNotFoundInAnyAssembly_ReturnsFalse()
        {
            Assert.IsFalse(TsModuleTestHarness.CallIsTsBehaviourType("NoSuchTypeAnywhere_XyzZy", "No.Such.Namespace"));
        }

        [Test]
        public void IsTsBehaviourType_NullOrEmptyNamespace_StillResolvesFullName()
        {
            // ns null/empty both fall back to the bare short name in the full-name lookup.
            Assert.IsTrue(TsModuleTestHarness.CallIsTsBehaviourType("StateManager", "Tsvrc.StateMachine"));
            Assert.IsFalse(TsModuleTestHarness.CallIsTsBehaviourType("StateManager", null));
            Assert.IsFalse(TsModuleTestHarness.CallIsTsBehaviourType("StateManager", ""));
        }
    }
}
