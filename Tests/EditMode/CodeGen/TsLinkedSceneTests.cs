using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor.SceneManagement;

namespace Tsvrc.Tests.EditMode
{
    // TsLinkedScene: the scene-scoping seam RunCore, HasBootstrapSignal, FindRoot, and every
    // module's LoadConfig() route scene-sourced lookups through. Every test here redirects
    // TsPaths.GeneratedFolder to scratch first (TsLinkedScene's own persisted asset is derived
    // from it) so this suite never reads or writes a consuming project's real
    // Assets/TsGenerated/TsLinkedSceneConfig.asset, regardless of whether this project has
    // actually configured one for real.
    public class TsLinkedSceneTests
    {
        [SetUp]
        public void SetUp()
        {
            ScratchAssets.EnsureFolder();
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/GeneratedScratch";
            TsLinkedScene.ClearOverride();
        }

        [TearDown]
        public void TearDown()
        {
            TsLinkedScene.ClearOverride();
            TsPaths.ResetToDefaults();
            ScratchAssets.DeleteAll();
        }

        [Test]
        public void IsConfigured_NoOverrideAndNoRealAssetYet_ReturnsFalse()
        {
            Assert.IsFalse(TsLinkedScene.IsConfigured);
        }

        [Test]
        public void ScenePath_Set_PersistsAndIsConfiguredBecomesTrue()
        {
            TsLinkedScene.ScenePath = "Assets/SomeScene.unity";

            Assert.IsTrue(TsLinkedScene.IsConfigured);
            Assert.AreEqual("Assets/SomeScene.unity", TsLinkedScene.ScenePath);
        }

        [Test]
        public void ScenePath_SetToNull_IsConfiguredBecomesFalseAgain()
        {
            TsLinkedScene.ScenePath = "Assets/SomeScene.unity";
            TsLinkedScene.ScenePath = null;

            Assert.IsFalse(TsLinkedScene.IsConfigured);
        }

        [Test]
        public void Override_TakesPrecedenceOverRealPersistedValue()
        {
            TsLinkedScene.ScenePath = "Assets/RealLinkedScene.unity";

            TsLinkedScene.SetOverride("");

            Assert.IsFalse(TsLinkedScene.IsConfigured,
                "An empty-string override (what TempSceneScope sets, since an unsaved scene's path is always \"\") " +
                "must make IsConfigured false even though a real value is persisted underneath it.");
        }

        [Test]
        public void IsConfiguredButNotLoaded_PathConfiguredButNoMatchingSceneLoaded_ReturnsTrue()
        {
            TsLinkedScene.ScenePath = "Assets/DoesNotExist/Nowhere.unity";

            Assert.IsTrue(TsLinkedScene.IsConfiguredButNotLoaded);
        }

        [Test]
        public void IsConfiguredButNotLoaded_NotConfiguredAtAll_ReturnsFalse()
        {
            Assert.IsFalse(TsLinkedScene.IsConfiguredButNotLoaded);
        }

        [Test]
        public void FindLoadedScene_ScenePathMatchesALoadedScene_ReturnsIt()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride(); // this test cares about the real path, not the scope's own override
            string scenePath = ScratchAssets.Folder + "/LinkedSceneTest.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);
            TsLinkedScene.ScenePath = scenePath;

            var found = TsLinkedScene.FindLoadedScene();

            Assert.IsTrue(found.HasValue);
            Assert.AreEqual(scenePath, found.Value.path);
            Assert.IsFalse(TsLinkedScene.IsConfiguredButNotLoaded);
        }

        [Test]
        public void Find_NotConfigured_FallsBackToAnyLoadedScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride(); // simulate a project that has never configured a linked scene
            var config = scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();

            var found = TsLinkedScene.Find<TsConfig>();

            Assert.AreSame(config, found);
        }

        [Test]
        public void Find_ConfiguredButNotLoaded_ReturnsNullEvenIfSomethingMatchesInAnotherScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();
            TsLinkedScene.ScenePath = "Assets/DoesNotExist/Nowhere.unity";

            var found = TsLinkedScene.Find<TsConfig>();

            Assert.IsNull(found,
                "A TsConfig existing in some other loaded scene must never be treated as a substitute " +
                "once a specific scene has been linked.");
        }

        // FindType(Type) is the non-generic twin of Find<T>(), used by TsModule.FindRoot() where
        // the compiled root's type is only known at runtime. Same fallback/scoping behavior.
        [Test]
        public void FindType_NotConfigured_FallsBackToAnyLoadedScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            var config = scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindType(typeof(TsConfig));

            Assert.AreSame(config, found);
        }

        [Test]
        public void FindType_ConfiguredAndLoaded_FindsComponentInThatScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/FindTypeTest.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);
            TsLinkedScene.ScenePath = scenePath;
            var config = scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindType(typeof(TsConfig));

            Assert.AreSame(config, found);
        }

        [Test]
        public void FindType_ConfiguredButNotLoaded_ReturnsNullEvenIfSomethingMatchesInAnotherScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();
            TsLinkedScene.ScenePath = "Assets/DoesNotExist/Nowhere.unity";

            var found = TsLinkedScene.FindType(typeof(TsConfig));

            Assert.IsNull(found);
        }
    }
}
