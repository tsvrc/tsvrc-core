using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEditor;

namespace Tsvrc.Tests.EditMode
{
    // Pure decision logic behind TsWindow's status box / primary action button - extracted as
    // internal static methods specifically so they're testable without driving OnGUI or an
    // EditorWindow instance (mirrors the reflection-free pattern already used elsewhere in this
    // suite for internal helpers, since InternalsVisibleTo already exposes Tsvrc.Editor here).
    public class TsWindowLogicTests
    {
        [Test]
        public void DetermineStatus_PlayMode_ReturnsWarningRegardlessOfOtherState()
        {
            // TsGenerator.Run() itself bails out during play mode, so this must win over every
            // other flag - a config existing or a bootstrap being pending doesn't change that
            // nothing here can actually run right now.
            var (message, type) = DetermineStatus(hasConfig: true, isBootstrapPending: true, isPlayMode: true);

            Assert.AreEqual(MessageType.Warning, type);
            StringAssert.Contains("Play Mode", message);
        }

        [Test]
        public void DetermineStatus_BootstrapPending_ReturnsInfoWaitingMessage()
        {
            var (message, type) = DetermineStatus(hasConfig: false, isBootstrapPending: true, isPlayMode: false);

            Assert.AreEqual(MessageType.Info, type);
            StringAssert.Contains("Setting up", message);
        }

        [Test]
        public void DetermineStatus_PendingTrueEvenWithConfig_StillReportsWaiting()
        {
            // A second write-then-stop pass (e.g. a config change during an in-flight bootstrap)
            // must still show "waiting", regardless of whether a TsConfig already exists.
            var (message, type) = DetermineStatus(hasConfig: true, isBootstrapPending: true, isPlayMode: false);

            Assert.AreEqual(MessageType.Info, type);
            StringAssert.Contains("Setting up", message);
        }

        [Test]
        public void DetermineStatus_NoConfigNotPending_ReturnsNotSetUpMessage()
        {
            var (message, type) = DetermineStatus(hasConfig: false, isBootstrapPending: false, isPlayMode: false);

            Assert.AreEqual(MessageType.Info, type);
            StringAssert.Contains("not yet set up", message);
        }

        [Test]
        public void DetermineStatus_ConfigExistsNotPending_ReturnsReadyMessage()
        {
            var (message, type) = DetermineStatus(hasConfig: true, isBootstrapPending: false, isPlayMode: false);

            StringAssert.Contains("set up", message);
            StringAssert.DoesNotContain("not yet", message);
        }

        [Test]
        public void DetermineActionLabel_NoConfig_ReturnsInitialize()
        {
            Assert.AreEqual("Initialize Tsvrc", TsWindow.DetermineActionLabel(hasConfig: false));
        }

        [Test]
        public void DetermineActionLabel_HasConfig_ReturnsForceRegenerate()
        {
            Assert.AreEqual("Force Regenerate", TsWindow.DetermineActionLabel(hasConfig: true));
        }

        [Test]
        public void IsActionEnabled_WhenBootstrapPending_ReturnsFalse()
        {
            Assert.IsFalse(TsWindow.IsActionEnabled(isBootstrapPending: true, isPlayMode: false));
        }

        [Test]
        public void IsActionEnabled_WhenPlayMode_ReturnsFalse()
        {
            Assert.IsFalse(TsWindow.IsActionEnabled(isBootstrapPending: false, isPlayMode: true));
        }

        [Test]
        public void IsActionEnabled_WhenNeitherPendingNorPlayMode_ReturnsTrue()
        {
            Assert.IsTrue(TsWindow.IsActionEnabled(isBootstrapPending: false, isPlayMode: false));
        }

        [Test]
        public void DetermineActionDisabledReason_PlayModeTakesPrecedenceOverPending()
        {
            string reason = TsWindow.DetermineActionDisabledReason(isBootstrapPending: true, isPlayMode: true);

            StringAssert.Contains("Play Mode", reason);
        }

        [Test]
        public void DetermineActionDisabledReason_PendingOnly_MentionsCompiling()
        {
            string reason = TsWindow.DetermineActionDisabledReason(isBootstrapPending: true, isPlayMode: false);

            StringAssert.Contains("compiling", reason);
        }

        [Test]
        public void DetermineActionDisabledReason_NeitherFlag_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineActionDisabledReason(isBootstrapPending: false, isPlayMode: false));
        }

        private static (string message, MessageType type) DetermineStatus(bool hasConfig, bool isBootstrapPending, bool isPlayMode) =>
            TsWindow.DetermineStatus(hasConfig, isBootstrapPending, isPlayMode);

        [Test]
        public void DetermineLinkedSceneWarning_NotConfiguredButNotLoaded_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineLinkedSceneWarning("Assets/Foo.unity", isConfiguredButNotLoaded: false));
        }

        [Test]
        public void DetermineLinkedSceneWarning_ConfiguredButNotLoaded_MentionsScenePath()
        {
            string message = TsWindow.DetermineLinkedSceneWarning("Assets/MoL/Scenes/MoL.unity", isConfiguredButNotLoaded: true);

            StringAssert.Contains("Assets/MoL/Scenes/MoL.unity", message);
        }

        [Test]
        public void DetermineCollisionWarning_NoCollisions_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineCollisionWarning(new List<string>()));
        }

        [Test]
        public void DetermineCollisionWarning_Null_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineCollisionWarning(null));
        }

        [Test]
        public void DetermineCollisionWarning_HasCollisions_MentionsFieldNamesAndAliasGuidance()
        {
            string message = TsWindow.DetermineCollisionWarning(new List<string> { "GameManager" });

            StringAssert.Contains("GameManager", message);
            StringAssert.Contains("__Alias__", message);
        }

        [Test]
        public void DetermineCollisionWarning_MultipleCollisions_ListsAllOfThem()
        {
            string message = TsWindow.DetermineCollisionWarning(new List<string> { "Foo", "Bar" });

            StringAssert.Contains("Foo", message);
            StringAssert.Contains("Bar", message);
            StringAssert.Contains("2 field name", message);
        }

        [Test]
        public void DetermineRunWarningsSummary_NoWarnings_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineRunWarningsSummary(new List<string>()));
        }

        [Test]
        public void DetermineRunWarningsSummary_Null_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineRunWarningsSummary(null));
        }

        [Test]
        public void DetermineRunWarningsSummary_FewerThanMax_ShowsAllOfThemWithoutMoreSuffix()
        {
            string message = TsWindow.DetermineRunWarningsSummary(new List<string> { "[FooModule] a", "[FooModule] b" }, maxShown: 3);

            StringAssert.Contains("[FooModule] a", message);
            StringAssert.Contains("[FooModule] b", message);
            StringAssert.DoesNotContain("more", message);
            StringAssert.Contains("2 issue(s)", message);
        }

        [Test]
        public void DetermineRunWarningsSummary_MoreThanMax_TruncatesAndMentionsRemainingCount()
        {
            string message = TsWindow.DetermineRunWarningsSummary(
                new List<string> { "[FooModule] a", "[FooModule] b", "[FooModule] c", "[FooModule] d" }, maxShown: 2);

            StringAssert.Contains("[FooModule] a", message);
            StringAssert.Contains("[FooModule] b", message);
            StringAssert.DoesNotContain("[FooModule] c", message);
            StringAssert.Contains("+2 more", message);
            StringAssert.Contains("Console", message);
        }
    }
}
