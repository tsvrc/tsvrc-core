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

        // DrawLinkedScene's own warning box, drawn directly above this one, already explains the
        // "configured but not loaded" state in full, so this shows nothing at all for that state
        // rather than a second, contradictory box ("click to get started" next to "open it").
        [Test]
        public void DetermineStatus_ConfiguredButNotLoaded_ReturnsNoMessage()
        {
            var (message, type) = TsWindow.DetermineStatus(
                hasConfig: false, isBootstrapPending: false, isPlayMode: false, isConfiguredButNotLoaded: true);

            Assert.IsNull(message);
        }

        [Test]
        public void DetermineStatus_ConfiguredButNotLoaded_TakesPrecedenceOverHasConfig()
        {
            var (message, type) = TsWindow.DetermineStatus(
                hasConfig: true, isBootstrapPending: false, isPlayMode: false, isConfiguredButNotLoaded: true);

            Assert.IsNull(message,
                "Even a config resolved via the legacy fallback must not produce a second, contradictory box.");
        }

        // A working setup (hasConfig true, e.g. via the legacy "search whatever scene is open"
        // fallback) that was never actually linked leaves the entire corruption-safety net
        // silently off, with nothing in the window telling the user to link it.
        [Test]
        public void DetermineStatus_HasConfigButNotLinked_WarnsToLinkThisScene()
        {
            var (message, type) = TsWindow.DetermineStatus(
                hasConfig: true, isBootstrapPending: false, isPlayMode: false, isLinked: false);

            Assert.AreEqual(MessageType.Warning, type);
            StringAssert.Contains("no scene is linked yet", message);
        }

        [Test]
        public void DetermineStatus_HasConfigAndIsLinked_ReturnsPlainReadyMessage()
        {
            var (message, type) = TsWindow.DetermineStatus(
                hasConfig: true, isBootstrapPending: false, isPlayMode: false, isLinked: true);

            Assert.AreEqual("Tsvrc is set up.", message);
        }

        [Test]
        public void DetermineActionLabel_NoConfig_ReturnsInitialize()
        {
            Assert.AreEqual("Initialize Tsvrc", TsWindow.DetermineActionLabel(hasConfig: false, isConfiguredButNotLoaded: false));
        }

        [Test]
        public void DetermineActionLabel_HasConfig_ReturnsForceRegenerate()
        {
            Assert.AreEqual("Force Regenerate", TsWindow.DetermineActionLabel(hasConfig: true, isConfiguredButNotLoaded: false));
        }

        [Test]
        public void DetermineActionLabel_ConfiguredButNotLoaded_ReturnsOpenLinkedSceneRegardlessOfConfig()
        {
            Assert.AreEqual("Open Linked Scene", TsWindow.DetermineActionLabel(hasConfig: false, isConfiguredButNotLoaded: true));
            Assert.AreEqual("Open Linked Scene", TsWindow.DetermineActionLabel(hasConfig: true, isConfiguredButNotLoaded: true));
        }

        // Once the linked scene asset is confirmed gone (not merely unloaded), "open it" is a
        // dead-end action; this must win over "not loaded" (a genuinely missing asset also
        // satisfies isConfiguredButNotLoaded, since IsConfiguredButMissing is a strict subset).
        [Test]
        public void DetermineActionLabel_ConfiguredButMissing_ReturnsLinkedSceneMissingNotOpenLinkedScene()
        {
            string label = TsWindow.DetermineActionLabel(hasConfig: false, isConfiguredButNotLoaded: true, isConfiguredButMissing: true);

            Assert.AreEqual("Linked Scene Missing", label);
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
        public void IsActionEnabled_WhenConfiguredButMissing_ReturnsFalse()
        {
            Assert.IsFalse(TsWindow.IsActionEnabled(isBootstrapPending: false, isPlayMode: false, isConfiguredButMissing: true));
        }

        [Test]
        public void IsActionEnabled_WhenNeitherPendingNorPlayModeNorMissing_ReturnsTrue()
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
        public void DetermineActionDisabledReason_ConfiguredButMissing_MentionsPickingANewScene()
        {
            string reason = TsWindow.DetermineActionDisabledReason(isBootstrapPending: false, isPlayMode: false, isConfiguredButMissing: true);

            StringAssert.Contains("no longer exists", reason);
        }

        [Test]
        public void DetermineActionDisabledReason_NeitherFlag_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineActionDisabledReason(isBootstrapPending: false, isPlayMode: false));
        }

        [Test]
        public void IsActionEnabled_WhenPendingChanges_ReturnsFalse()
        {
            // Force Regenerate/Initialize Tsvrc and the pending-changes footer's own Apply button
            // must never both be live triggers for a regenerate at once - see IsActionEnabled's
            // own comment for why that used to leave the Apply/Discard footer stuck.
            Assert.IsFalse(TsWindow.IsActionEnabled(isBootstrapPending: false, isPlayMode: false, hasPendingChanges: true));
        }

        [Test]
        public void IsActionEnabled_NoPendingChanges_ReturnsTrue()
        {
            Assert.IsTrue(TsWindow.IsActionEnabled(isBootstrapPending: false, isPlayMode: false, hasPendingChanges: false));
        }

        [Test]
        public void DetermineActionDisabledReason_PendingChangesOnly_MentionsApplyingOrDiscarding()
        {
            string reason = TsWindow.DetermineActionDisabledReason(isBootstrapPending: false, isPlayMode: false, hasPendingChanges: true);

            StringAssert.Contains("Apply", reason);
            StringAssert.Contains("discard", reason);
        }

        [Test]
        public void DetermineActionDisabledReason_PlayModeTakesPrecedenceOverPendingChanges()
        {
            string reason = TsWindow.DetermineActionDisabledReason(isBootstrapPending: false, isPlayMode: true, hasPendingChanges: true);

            StringAssert.Contains("Play Mode", reason);
        }

        private static (string message, MessageType type) DetermineStatus(bool hasConfig, bool isBootstrapPending, bool isPlayMode) =>
            TsWindow.DetermineStatus(hasConfig, isBootstrapPending, isPlayMode);

        [Test]
        public void DetermineLinkedSceneWarning_NotConfiguredButNotLoaded_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineLinkedSceneWarning("Assets/Foo.unity", isConfiguredButNotLoaded: false, isConfiguredButMissing: false));
        }

        [Test]
        public void DetermineLinkedSceneWarning_ConfiguredButNotLoaded_MentionsScenePath()
        {
            string message = TsWindow.DetermineLinkedSceneWarning("Assets/MoL/Scenes/MoL.unity", isConfiguredButNotLoaded: true, isConfiguredButMissing: false);

            StringAssert.Contains("Assets/MoL/Scenes/MoL.unity", message);
        }

        // A genuinely deleted linked scene asset gets its own message: "open it" isn't a real
        // recovery action once the asset itself is gone, so this must win over the generic "not
        // currently open" wording even though IsConfiguredButMissing implies NotLoaded too.
        [Test]
        public void DetermineLinkedSceneWarning_ConfiguredButMissing_ReturnsDistinctMessageNotTheNotLoadedOne()
        {
            string message = TsWindow.DetermineLinkedSceneWarning("Assets/Gone.unity", isConfiguredButNotLoaded: true, isConfiguredButMissing: true);

            StringAssert.Contains("no longer exists", message);
            StringAssert.DoesNotContain("not currently open", message);
        }

        // TsLinkedSceneConfig.asset lost (a bad clone, an accidental delete) while real generated
        // content still sits on disk proves a link used to exist and is now gone, which needs a
        // loud warning instead of silently reverting to legacy unscoped behavior with no symptom
        // at all.
        [Test]
        public void DetermineMissingLinkWarning_NotConfiguredButGeneratedContentExists_WarnsLoudly()
        {
            string message = TsWindow.DetermineMissingLinkWarning(isConfigured: false, generatedScaffoldFileExists: true);

            StringAssert.Contains("TsLinkedSceneConfig.asset", message);
        }

        [Test]
        public void DetermineMissingLinkWarning_NotConfiguredAndNoGeneratedContent_ReturnsNull()
        {
            // The ordinary first-run case: nothing to warn about, this is the expected default.
            Assert.IsNull(TsWindow.DetermineMissingLinkWarning(isConfigured: false, generatedScaffoldFileExists: false));
        }

        [Test]
        public void DetermineMissingLinkWarning_Configured_ReturnsNullRegardlessOfGeneratedContent()
        {
            Assert.IsNull(TsWindow.DetermineMissingLinkWarning(isConfigured: true, generatedScaffoldFileExists: true));
        }

        // A missing TsBuiltinConfig.asset (package-shipped, so a bad submodule update or merge
        // can lose it for the whole team at once) silently drops every library-provided
        // Global/pool prefab/Factory group with no other symptom.
        [Test]
        public void DetermineBuiltinConfigWarning_Missing_MentionsTheAssetPath()
        {
            string message = TsWindow.DetermineBuiltinConfigWarning(isBuiltinConfigMissing: true);

            StringAssert.Contains(TsModule.BuiltinConfigPath, message);
        }

        [Test]
        public void DetermineBuiltinConfigWarning_NotMissing_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineBuiltinConfigWarning(isBuiltinConfigMissing: false));
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
        public void DetermineCollisionWarning_HasCollisions_MentionsFieldNamesAndNameGuidance()
        {
            string message = TsWindow.DetermineCollisionWarning(new List<string> { "GameManager" });

            StringAssert.Contains("GameManager", message);
            StringAssert.Contains("Name", message);
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

        [Test]
        public void DetermineTreeShakingSummary_NoExclusions_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineTreeShakingSummary(new List<string>()));
        }

        [Test]
        public void DetermineTreeShakingSummary_Null_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineTreeShakingSummary(null));
        }

        [Test]
        public void DetermineTreeShakingSummary_FewerThanMax_ListsAllExcludedNames()
        {
            string message = TsWindow.DetermineTreeShakingSummary(new List<string> { "GameManager", "CreateWidget" }, maxShown: 5);

            StringAssert.Contains("GameManager", message);
            StringAssert.Contains("CreateWidget", message);
            StringAssert.Contains("2 unused entries", message);
            StringAssert.DoesNotContain("more", message);
        }

        [Test]
        public void DetermineTreeShakingSummary_SingleExclusion_UsesSingularWording()
        {
            string message = TsWindow.DetermineTreeShakingSummary(new List<string> { "GameManager" });

            StringAssert.Contains("1 unused entry", message);
        }

        [Test]
        public void DetermineTreeShakingSummary_MoreThanMax_TruncatesAndMentionsRemainingCount()
        {
            string message = TsWindow.DetermineTreeShakingSummary(
                new List<string> { "A", "B", "C", "D" }, maxShown: 2);

            StringAssert.Contains("A", message);
            StringAssert.Contains("B", message);
            StringAssert.DoesNotContain("C", message);
            StringAssert.Contains("+2 more", message);
        }

        [Test]
        public void DetermineTreeShakingGraceSummary_NoneKept_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineTreeShakingGraceSummary(new List<string>()));
        }

        [Test]
        public void DetermineTreeShakingGraceSummary_Null_ReturnsNull()
        {
            Assert.IsNull(TsWindow.DetermineTreeShakingGraceSummary(null));
        }

        [Test]
        public void DetermineTreeShakingGraceSummary_FewerThanMax_ListsAllNamesAndReadsAsReassurance()
        {
            string message = TsWindow.DetermineTreeShakingGraceSummary(new List<string> { "GameManager", "CreateWidget" }, maxShown: 5);

            StringAssert.Contains("GameManager", message);
            StringAssert.Contains("CreateWidget", message);
            StringAssert.Contains("kept for now", message);
            StringAssert.Contains("normal right after registering", message);
            StringAssert.DoesNotContain("more", message);
        }

        [Test]
        public void DetermineTreeShakingGraceSummary_SingleEntry_UsesSingularWording()
        {
            string message = TsWindow.DetermineTreeShakingGraceSummary(new List<string> { "GameManager" });

            StringAssert.Contains("1 entry isn't referenced", message);
        }

        [Test]
        public void DetermineTreeShakingGraceSummary_MoreThanMax_TruncatesAndMentionsRemainingCount()
        {
            string message = TsWindow.DetermineTreeShakingGraceSummary(
                new List<string> { "A", "B", "C", "D" }, maxShown: 2);

            StringAssert.Contains("A", message);
            StringAssert.Contains("B", message);
            StringAssert.DoesNotContain("C", message);
            StringAssert.Contains("+2 more", message);
        }
    }
}
