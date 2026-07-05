using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using TMPro;

namespace Tsvrc.Tests.Editor
{
    // TranslationModule.FindTmpTargets() against real scene TextMeshProUGUI objects.
    // Confirms BOTH the name-shape regex AND translation-key presence gate every match -
    // a correctly-shaped name with no matching key is excluded, and vice versa. Phase G3.7.
    public class TranslationModuleFindTmpTargetsTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static IList FindTmpTargets(TranslationModule module)
            => (IList)PrivateFieldAccess.InvokeInstance(module, "FindTmpTargets");

        private static TranslationModule BuildModuleWithKeys(params string[] keys)
        {
            var module = new TranslationModule();
            var set = new HashSet<string>(keys, System.StringComparer.Ordinal);
            PrivateFieldAccess.SetField(module, "_translationKeys", set);
            return module;
        }

        [Test]
        public void FindTmpTargets_NameMatchesShapeAndHasKnownKey_IsIncluded()
        {
            var module = BuildModuleWithKeys("_hello_");
            _scope.CreateGameObject("_hello_").AddComponent<TextMeshProUGUI>();

            var result = FindTmpTargets(module);

            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void FindTmpTargets_NameMatchesShapeButKeyNotInAnyLanguage_IsSilentlyExcluded()
        {
            var module = BuildModuleWithKeys("_other_");
            _scope.CreateGameObject("_hello_").AddComponent<TextMeshProUGUI>();

            var result = FindTmpTargets(module);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void FindTmpTargets_KeyExistsButNameMissingLeadingUnderscore_IsExcluded()
        {
            var module = BuildModuleWithKeys("hello_");
            _scope.CreateGameObject("hello_").AddComponent<TextMeshProUGUI>();

            var result = FindTmpTargets(module);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void FindTmpTargets_KeyExistsButNameMissingTrailingUnderscore_IsExcluded()
        {
            var module = BuildModuleWithKeys("_hello");
            _scope.CreateGameObject("_hello").AddComponent<TextMeshProUGUI>();

            var result = FindTmpTargets(module);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void FindTmpTargets_CaseMismatch_IsExcludedOrdinalComparison()
        {
            var module = BuildModuleWithKeys("_Hello_");
            _scope.CreateGameObject("_hello_").AddComponent<TextMeshProUGUI>();

            var result = FindTmpTargets(module);

            Assert.AreEqual(0, result.Count);
        }
    }
}
