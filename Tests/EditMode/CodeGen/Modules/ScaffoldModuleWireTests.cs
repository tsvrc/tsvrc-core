using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Core.Generated;
using Tsvrc.Editor;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Tests ScaffoldModule.AfterFilesStable(), the internal entry point that owns
    // EnsureRootSceneObject()/EnsureChildSceneObject()/NormalizeProgramAsset(), none of
    // which are individually public.
    //
    // Redirects TsPaths so AfterFilesStable()'s own EnsureUdonSharpProgramAsset/
    // FindCompiledType calls resolve against TestGenerated (a permanent, already-compiled
    // double, see TestGenerated.cs) instead of a consuming project's real TsGenerated. The
    // script path points at TestGenerated.cs's real, permanent location (so a real MonoScript
    // is found), while the program asset itself is created fresh under ScratchAssets.Folder
    // each test and deleted afterward, never the real project's Assets/TsGenerated.
    public class ScaffoldModuleWireTests
    {
        private const string TestGeneratedScriptPath = "Assets/Tsvrc/Tests/TestDoubles/CodeGen/TestGenerated.cs";

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            TsPaths.CompiledClassName = nameof(TestGenerated);
            TsPaths.GeneratedFolder = ScratchAssets.Folder;
            TsPaths.ScaffoldScriptPath = TestGeneratedScriptPath;
            // ScaffoldAssetPath stays derived (null): GeneratedFolder + CompiledClassName + ".asset",
            // meaning under the scratch folder, see ScaffoldModule.GeneratedAssetPath.
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose(); // also resets TsPaths to defaults
            ScratchAssets.DeleteAll();
        }

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
            // only pins "exactly one survives, and it's genuinely one of the two originals",
            // not specifically which one. EnsureRootSceneObject keeps whichever the engine
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
            // "Real" here means a genuine UdonSharpProgramAsset created through production
            // code (EnsureUdonSharpProgramAsset), not a hand-built ScriptableObject, just one
            // created fresh under the scratch folder for this test, never the actual project's.
            Assert.IsTrue(ScaffoldModule.EnsureUdonSharpProgramAsset(TestGeneratedScriptPath, ScratchAssets.Folder + "/Normalize.asset"));
            var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(ScratchAssets.Folder + "/Normalize.asset");
            Assert.IsNotNull(asset);

            ScaffoldModule.NormalizeProgramAsset(asset); // settle into sorted order first
            bool secondCall = ScaffoldModule.NormalizeProgramAsset(asset);

            Assert.IsFalse(secondCall, "Once sorted, re-normalizing must be a no-op.");
        }
    }
}
