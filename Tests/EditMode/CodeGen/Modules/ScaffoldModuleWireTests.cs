using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Core.Generated;
using Tsvrc.Editor;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

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
            // UdonSharpUndo.AddComponent needs a real program asset for TestGenerated to already
            // exist - normally AfterFilesStable() itself creates one (see its own
            // EnsureUdonSharpProgramAsset call), but that hasn't run yet at this point in the
            // test, so it's created here first at the exact path AfterFilesStable() would use.
            // EnsureUdonSharpProgramAsset is idempotent, so AfterFilesStable()'s own call below
            // is a safe no-op against this same asset.
            Assert.IsTrue(ScaffoldModule.EnsureUdonSharpProgramAsset(TestGeneratedScriptPath, ScratchAssets.Folder + "/TestGenerated.asset"));

            var first = _scope.CreateGameObject("First");
            UdonSharpUndo.AddComponent(first, CompiledType);
            var second = _scope.CreateGameObject("Second");
            UdonSharpUndo.AddComponent(second, CompiledType);

            // Now logged before destroying; see the dedicated test below for the message's exact
            // content. This only needs to not fail on the now-expected warning (Unity's test
            // framework fails a test on any unhandled Warning/Error log by default).
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Multiple.*instances found.*"));
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
        public void AfterFilesStable_MultipleExistingInstances_LogsWhichSurvivesAndWhichAreDestroyed()
        {
            Assert.IsTrue(ScaffoldModule.EnsureUdonSharpProgramAsset(TestGeneratedScriptPath, ScratchAssets.Folder + "/TestGenerated.asset"));

            var first = _scope.CreateGameObject("First");
            UdonSharpUndo.AddComponent(first, CompiledType);
            var second = _scope.CreateGameObject("Second");
            UdonSharpUndo.AddComponent(second, CompiledType);

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                $@"\[TsGenerator\] Multiple {ScaffoldModule.CompiledClassName} instances found\. Keeping '(First|Second)', destroying '(First|Second)'\."));

            new ScaffoldModule().AfterFilesStable();
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

        // A TsConfig dragged out from under the root (still in the same scene, just no longer a
        // direct child) must be found and moved back, preserving its existing data, rather than
        // creating a second, empty, orphaned duplicate.
        [Test]
        public void AfterFilesStable_TsConfigExistsElsewhereInScene_ReparentsExistingInstanceRatherThanCreatingNew()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var elsewhere = _scope.CreateGameObject("MovedOut");
            var existingConfig = elsewhere.AddComponent<TsConfig>();

            new ScaffoldModule().AfterFilesStable();

            Assert.AreEqual(1, root.transform.childCount, "Must not create a second, empty TsConfig.");
            var child = root.transform.Find("TsConfig");
            Assert.IsNotNull(child, "The reparented object must also be renamed so the next pass finds it directly.");
            Assert.AreSame(existingConfig, child.GetComponent<TsConfig>(),
                "Must be the exact same TsConfig instance, not a freshly created one - this is what preserves its data.");
        }

        [Test]
        public void AfterFilesStable_TsConfigChildAlreadyUnderRootButMisnamed_RenamesInPlace()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var misnamed = _scope.CreateGameObject("NotTsConfigYet");
            misnamed.transform.SetParent(root.transform, false);
            var existingConfig = misnamed.AddComponent<TsConfig>();

            new ScaffoldModule().AfterFilesStable();

            Assert.AreEqual(1, root.transform.childCount);
            Assert.AreEqual("TsConfig", misnamed.name);
            Assert.AreSame(existingConfig, root.transform.Find("TsConfig").GetComponent<TsConfig>());
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

        // The first successful bootstrap in a scene should auto-link it, so the corruption-safety
        // net TsLinkedScene provides isn't silently opt-in forever. AutoLinkSceneIfUnconfigured
        // isn't itself public, so this drives it through the same real entry point
        // (AfterFilesStable) every other test in this class already uses.
        [Test]
        public void AfterFilesStable_RootCreatedInUnconfiguredButSavedScene_AutoLinksToThatScene()
        {
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/AutoLinkTest.unity";
            EditorSceneManager.SaveScene(_scope.Scene, scenePath);
            Assert.IsFalse(TsLinkedScene.IsConfigured, "Precondition: nothing linked yet.");

            new ScaffoldModule().AfterFilesStable();

            Assert.IsTrue(TsLinkedScene.IsConfigured);
            Assert.AreEqual(scenePath, TsLinkedScene.ScenePath);
        }

        [Test]
        public void AfterFilesStable_RootCreatedInUnsavedScene_DoesNotAutoLink()
        {
            TsLinkedScene.ClearOverride();
            // _scope.Scene is unsaved by default (path == ""), so there's no real scene asset to
            // link to yet - auto-linking must wait for a genuine, saved scene.

            new ScaffoldModule().AfterFilesStable();

            Assert.IsFalse(TsLinkedScene.IsConfigured);
        }

        [Test]
        public void AfterFilesStable_AlreadyLinkedElsewhere_DoesNotOverwriteExistingLink()
        {
            TsLinkedScene.ClearOverride();
            string alreadyLinkedPath = ScratchAssets.Folder + "/AlreadyLinked.unity";
            EditorSceneManager.SaveScene(_scope.Scene, alreadyLinkedPath);
            TsLinkedScene.ScenePath = alreadyLinkedPath;

            new ScaffoldModule().AfterFilesStable();

            Assert.AreEqual(alreadyLinkedPath, TsLinkedScene.ScenePath,
                "An existing link must never be silently replaced by a later bootstrap pass.");
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
