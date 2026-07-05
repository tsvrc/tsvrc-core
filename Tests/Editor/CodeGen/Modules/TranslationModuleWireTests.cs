using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using TMPro;
using UnityEditor;
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
            // compiled root - this project has no real language files configured by default
            // (see CODEGEN_TESTING_PLAN.md Part 4.5), so the field genuinely doesn't exist
            // unless CodeGenSandbox.Bootstrap() has run; when it has, this same missing-field
            // scenario can't be constructed this way anymore, so this test is skipped (not
            // failed) in that state - see Wire_RealTranslationTargetsField_... below for the
            // bootstrapped-state coverage instead.
            var root = CompiledRootFixture.AddTo(_scope);
            if (new SerializedObject(root).FindProperty("_translationTargets") != null)
                Assert.Ignore("_translationTargets already exists on the compiled root (CodeGenSandbox is active) - this scenario is covered by Wire_RealTranslationTargetsField_... instead.");

            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_languages", PrivateFieldAccess.BuildList(EntryType, new[] { OneLanguage() }));

            LogAssert.NoUnexpectedReceived();
            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void Wire_RealTranslationTargetsField_IsAssignedToMatchingSceneTmpObjects()
        {
            // Runs for real once CodeGenSandbox.Bootstrap() has produced a real
            // "_translationTargets" field on TsvrcGenerated; Assert.Ignore()s otherwise.
            var root = CompiledRootFixture.AddTo(_scope);
            SandboxGate.RequireField(root, "_translationTargets");

            var target = _scope.CreateGameObject(CodeGenSandbox.TranslationKey).AddComponent<TextMeshProUGUI>();
            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_languages", PrivateFieldAccess.BuildList(EntryType, new[] { OneLanguage() }));
            PrivateFieldAccess.SetField(module, "_cachedTmpTargets", new List<TextMeshProUGUI> { target });

            module.Wire();

            var prop = new SerializedObject(root).FindProperty("_translationTargets");
            Assert.AreEqual(1, prop.arraySize);
            Assert.AreEqual(target, prop.GetArrayElementAtIndex(0).objectReferenceValue);
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
