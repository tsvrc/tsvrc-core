using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using TMPro;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // TranslationModule.Wire()/OnSceneHierarchyChanged() against a real compiled root.
    // Phase G4.11/G4.12/G5.6.
    public class TranslationModuleWireTests
    {
        private static readonly Type EntryType = PrivateFieldAccess.NestedType(typeof(TranslationModule), "LanguageEntry");

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static object OneLanguage()
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal) { ["_a_"] = "A" };
            return PrivateFieldAccess.BuildEntry(EntryType, ("Key", "en"), ("Label", "English"), ("Entries", dict));
        }

        [Test]
        public void Wire_EmptyLanguages_IsANoOpEvenWithoutARoot()
        {
            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_languages", PrivateFieldAccess.BuildList(EntryType, Array.Empty<object>()));

            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void Wire_NonEmptyLanguagesButTranslationTargetsFieldMissing_SilentNoOpNoWarning()
        {
            // Unlike every other config-driven module, TranslationModule.Wire() returns
            // silently (no Debug.LogWarning) when `_translationTargets` isn't found on the
            // compiled root - this project has no real language files configured yet, so
            // the field genuinely doesn't exist (see CODEGEN_TESTING_PLAN.md Part 4.5).
            CompiledRootFixture.AddTo(_scope);
            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_languages", PrivateFieldAccess.BuildList(EntryType, new[] { OneLanguage() }));

            LogAssert.NoUnexpectedReceived();
            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void SyncEffectiveKeys_SameNameSetAcrossHierarchyChurn_ReturnsFalse()
        {
            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_translationKeys", new HashSet<string>(new[] { "_a_" }, StringComparer.Ordinal));
            var first = _scope.CreateGameObject("_a_").AddComponent<TextMeshProUGUI>();
            PrivateFieldAccess.SetField(module, "_cachedTmpTargets", new List<TextMeshProUGUI> { first });
            PrivateFieldAccess.SetField(module, "_effectiveKeys", new HashSet<string>(new[] { "_a_" }, StringComparer.Ordinal));

            bool changed = (bool)PrivateFieldAccess.InvokeInstance(module, "SyncEffectiveKeys");

            Assert.IsFalse(changed);
        }

        [Test]
        public void SyncEffectiveKeys_NewTargetNameAppears_ReturnsTrue()
        {
            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_translationKeys", new HashSet<string>(new[] { "_a_", "_b_" }, StringComparer.Ordinal));
            _scope.CreateGameObject("_a_").AddComponent<TextMeshProUGUI>();
            _scope.CreateGameObject("_b_").AddComponent<TextMeshProUGUI>(); // new since last sync
            PrivateFieldAccess.SetField(module, "_cachedTmpTargets", new List<TextMeshProUGUI>());
            PrivateFieldAccess.SetField(module, "_effectiveKeys", new HashSet<string>(new[] { "_a_" }, StringComparer.Ordinal));

            bool changed = (bool)PrivateFieldAccess.InvokeInstance(module, "SyncEffectiveKeys");

            Assert.IsTrue(changed);
        }

        [Test]
        public void SyncEffectiveKeys_TargetRemoved_ReturnsTrue()
        {
            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_translationKeys", new HashSet<string>(new[] { "_a_" }, StringComparer.Ordinal));
            // Scene now has nothing matching - previously had "_a_".
            PrivateFieldAccess.SetField(module, "_cachedTmpTargets", new List<TextMeshProUGUI>());
            PrivateFieldAccess.SetField(module, "_effectiveKeys", new HashSet<string>(new[] { "_a_" }, StringComparer.Ordinal));

            bool changed = (bool)PrivateFieldAccess.InvokeInstance(module, "SyncEffectiveKeys");

            Assert.IsTrue(changed);
        }
    }
}
