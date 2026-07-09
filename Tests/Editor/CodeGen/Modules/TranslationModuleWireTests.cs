using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // TranslationModule.Wire()/OnSceneHierarchyChanged() against a real compiled root.
    public class TranslationModuleWireTests
    {
        private static readonly Type EntryType = CodeGenModuleReflection.NestedType(typeof(TranslationModule), "LanguageEntry");

        // A stand-in for the compiled root exposing only the one literal field name
        // AssignTargets is fed via SerializedProperty - not the real compiled type, since
        // this project has no real language config and thus no real "_translationTargets"
        // field outside a full config-driven generation pass.
        private class TranslationTargetsFieldDouble : MonoBehaviour
        {
            public TMPro.TextMeshProUGUI[] _translationTargets;
        }

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static object OneLanguage()
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal) { ["_a_"] = "A" };
            return CodeGenModuleReflection.BuildEntry(EntryType, ("Key", "en"), ("Label", "English"), ("Entries", dict));
        }

        [Test]
        public void Wire_EmptyLanguages_IsANoOpEvenWithoutARoot()
        {
            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_languages", CodeGenModuleReflection.BuildList(EntryType, Array.Empty<object>()));

            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void Wire_NonEmptyLanguagesButTranslationTargetsFieldMissing_SilentNoOpNoWarning()
        {
            // Unlike every other config-driven module, TranslationModule.Wire() returns
            // silently (no Debug.LogWarning) when `_translationTargets` isn't found on the
            // compiled root - this project has no real language files configured, so the
            // field genuinely never exists on the compiled type.
            CompiledRootFixture.AddTo(_scope);

            var module = new TranslationModule();
            PrivateFieldAccess.SetField(module, "_languages", CodeGenModuleReflection.BuildList(EntryType, new[] { OneLanguage() }));

            LogAssert.NoUnexpectedReceived();
            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void AssignTargets_ResizesArrayAndAssignsEachElement()
        {
            // AssignTargets is testable against any array-typed SerializedProperty, not only
            // the real compiled root's "_translationTargets" (which this project never has
            // without a real language config driving a full generation pass).
            var fakeRoot = _scope.CreateGameObject("FakeRoot").AddComponent<TranslationTargetsFieldDouble>();
            var target = _scope.CreateGameObject("Target").AddComponent<TextMeshProUGUI>();
            var prop = new SerializedObject(fakeRoot).FindProperty(nameof(TranslationTargetsFieldDouble._translationTargets));

            PrivateFieldAccess.InvokeStatic(typeof(TranslationModule), "AssignTargets", prop, new List<TextMeshProUGUI> { target });
            prop.serializedObject.ApplyModifiedPropertiesWithoutUndo();

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
            // Scene has nothing matching the tracked key "_a_".
            PrivateFieldAccess.SetField(module, "_cachedTmpTargets", new List<TextMeshProUGUI>());
            PrivateFieldAccess.SetField(module, "_effectiveKeys", new HashSet<string>(new[] { "_a_" }, StringComparer.Ordinal));

            bool changed = (bool)PrivateFieldAccess.InvokeInstance(module, "SyncEffectiveKeys");

            Assert.IsTrue(changed);
        }
    }
}
