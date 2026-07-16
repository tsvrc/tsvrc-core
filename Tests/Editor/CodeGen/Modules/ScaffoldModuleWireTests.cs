using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // ScaffoldModule.AfterFilesStable() - the internal entry point that owns
    // EnsureRootSceneObject()/EnsureChildSceneObject()/NormalizeProgramAsset(), none of
    // which are individually public.
    public class ScaffoldModuleWireTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static System.Type CompiledType => ScaffoldModule.FindCompiledType();

        [Test]
        public void AfterFilesStable_NoExistingInstanceAndNoSameNamedObject_CreatesNewRootAtSiblingIndexZero()
        {
            _scope.CreateGameObject("SomethingElseFirst");

            new ScaffoldModule().AfterFilesStable();

            var instances = Object.FindObjectsOfType(CompiledType, true);
            Assert.AreEqual(1, instances.Length);
            var root = ((Component)instances[0]).transform;
            Assert.AreEqual(ScaffoldModule.CompiledClassName, root.gameObject.name);
            Assert.AreEqual(0, root.GetSiblingIndex());
        }

        [Test]
        public void AfterFilesStable_SameNamedGameObjectWithoutComponent_ComponentAddedNotRecreated()
        {
            var existing = _scope.CreateGameObject(ScaffoldModule.CompiledClassName);

            new ScaffoldModule().AfterFilesStable();

            var instances = Object.FindObjectsOfType(CompiledType, true);
            Assert.AreEqual(1, instances.Length);
            Assert.AreEqual(existing, ((Component)instances[0]).gameObject, "The existing GameObject must be reused, not replaced.");
        }

        [Test]
        public void AfterFilesStable_MultipleExistingInstances_KeepsFirstDestroysRest()
        {
            var first = _scope.CreateGameObject("First");
            UdonSharpUndo.AddComponent(first, CompiledType);
            var second = _scope.CreateGameObject("Second");
            UdonSharpUndo.AddComponent(second, CompiledType);

            new ScaffoldModule().AfterFilesStable();

            var instances = Object.FindObjectsOfType(CompiledType, true);
            // FindObjectsOfType's element order isn't documented as creation order, so this
            // only pins "exactly one survives, and it's genuinely one of the two originals"
            // - not specifically which one. EnsureRootSceneObject keeps whichever the engine
            // reports at index 0 and destroys the rest.
            Assert.AreEqual(1, instances.Length);
            var survivor = ((Component)instances[0]).gameObject;
            Assert.That(survivor == first || survivor == second, "The surviving instance must be one of the two originals, not a new one.");
        }

        [Test]
        public void AfterFilesStable_TsConfigChildAbsent_IsCreatedAndTaggedEditorOnly()
        {
            var root = CompiledRootFixture.AddTo(_scope);

            new ScaffoldModule().AfterFilesStable();

            var child = root.transform.Find("TsConfig");
            Assert.IsNotNull(child);
            Assert.IsNotNull(child.GetComponent<TsConfig>());
            Assert.AreEqual("EditorOnly", child.gameObject.tag);
        }

        [Test]
        public void AfterFilesStable_TsConfigChildPresentWithoutComponent_ComponentAddedToExistingChild()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var existingChild = _scope.CreateGameObject("TsConfig");
            existingChild.transform.SetParent(root.transform, false);

            new ScaffoldModule().AfterFilesStable();

            Assert.AreEqual(1, root.transform.childCount, "Must not create a second TsConfig child.");
            Assert.IsNotNull(root.transform.Find("TsConfig").GetComponent<TsConfig>());
        }

        [Test]
        public void AfterFilesStable_TsConfigChildWithWrongTag_TagSelfHeals()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var existingChild = _scope.CreateGameObject("TsConfig");
            existingChild.transform.SetParent(root.transform, false);
            existingChild.AddComponent<TsConfig>();
            existingChild.tag = "Untagged";

            new ScaffoldModule().AfterFilesStable();

            Assert.AreEqual("EditorOnly", root.transform.Find("TsConfig").gameObject.tag);
        }

        [Test]
        public void NormalizeProgramAsset_Null_ReturnsFalseWithoutThrowing()
        {
            bool result = false;
            Assert.DoesNotThrow(() => result = ScaffoldModule.NormalizeProgramAsset(null));
            Assert.IsFalse(result);
        }

        [Test]
        public void NormalizeProgramAsset_CalledTwiceOnRealAsset_SecondCallIsNoOp()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>("Assets/TsGenerated/TsGenerated.asset");
            Assert.IsNotNull(asset, "This project's own bootstrap TsGenerated.asset must already exist (see CompiledRootFixture).");

            ScaffoldModule.NormalizeProgramAsset(asset); // settle into sorted order first
            bool secondCall = ScaffoldModule.NormalizeProgramAsset(asset);

            Assert.IsFalse(secondCall, "Once sorted, re-normalizing must be a no-op.");
        }
    }
}
