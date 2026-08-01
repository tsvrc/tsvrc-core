using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;
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

        // Saves scope's scene to a real scratch path, links it via TsLinkedScene.ScenePath, then
        // swaps the active scene away so it's genuinely unloaded rather than merely unsaved.
        // Deliberately does NOT dispose scope itself - TempSceneScope.Dispose() resets
        // TsPaths.GeneratedFolder to its defaults, which would silently redirect
        // TsLinkedSceneConfig.asset back to this project's real Assets/TsGenerated mid-test. The
        // class-level TearDown above already resets TsPaths once, safely, after every test ends.
        private static string LinkAndUnload(TempSceneScope scope, string fileName)
        {
            string path = $"{ScratchAssets.Folder}/{fileName}.unity";
            EditorSceneManager.SaveScene(scope.Scene, path);
            TsLinkedScene.ScenePath = path;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return path;
        }

        [Test]
        public void IsConfigured_NoOverrideAndNoRealAssetYet_ReturnsFalse()
        {
            Assert.IsFalse(TsLinkedScene.IsConfigured);
        }

        // ScenePath is stored indirectly, as the scene asset's GUID (see TsLinkedSceneConfig),
        // so setting it to a path with no real backing asset can't persist anything meaningful -
        // AssetPathToGUID has nothing to resolve. Production code only ever sets this from a real
        // SceneAsset field's own AssetDatabase.GetAssetPath (TsWindow.DrawLinkedScene), so every
        // test that wants a genuinely "configured" state links a real scratch scene instead.
        [Test]
        public void ScenePath_SetToRealSceneAsset_PersistsAndIsConfiguredBecomesTrue()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/RealScene.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);

            TsLinkedScene.ScenePath = scenePath;

            Assert.IsTrue(TsLinkedScene.IsConfigured);
            Assert.AreEqual(scenePath, TsLinkedScene.ScenePath);
        }

        [Test]
        public void ScenePath_SetToPathWithNoRealAsset_NeverPersistsAndIsConfiguredStaysFalse()
        {
            TsLinkedScene.ScenePath = "Assets/DoesNotExist/Nowhere.unity";

            Assert.IsFalse(TsLinkedScene.IsConfigured,
                "There is no real SceneAsset at this path to resolve a GUID from, so nothing should " +
                "be recorded - production code (TsWindow) only ever sets this from a real asset reference.");
        }

        [Test]
        public void ScenePath_SetToNull_IsConfiguredBecomesFalseAgain()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/ToClear.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);
            TsLinkedScene.ScenePath = scenePath;

            TsLinkedScene.ScenePath = null;

            Assert.IsFalse(TsLinkedScene.IsConfigured);
        }

        [Test]
        public void ScenePath_AssetRenamedOnDisk_ResolvesToNewPathAutomatically()
        {
            // Renaming/moving the linked scene asset must never break the link, since the GUID
            // (what's actually persisted) is unaffected by either.
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string originalPath = ScratchAssets.Folder + "/BeforeRename.unity";
            EditorSceneManager.SaveScene(scope.Scene, originalPath);
            TsLinkedScene.ScenePath = originalPath;

            string renamedPath = ScratchAssets.Folder + "/AfterRename.unity";
            string error = AssetDatabase.MoveAsset(originalPath, renamedPath);

            Assert.IsEmpty(error, "Precondition: the rename itself must succeed.");
            Assert.AreEqual(renamedPath, TsLinkedScene.ScenePath,
                "ScenePath must resolve to wherever the asset actually is now, with no separate resync step.");
            Assert.IsFalse(TsLinkedScene.IsConfiguredButMissing);
        }

        [Test]
        public void Override_TakesPrecedenceOverRealPersistedValue()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/RealLinkedScene.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);
            TsLinkedScene.ScenePath = scenePath;

            TsLinkedScene.SetOverride("");

            Assert.IsFalse(TsLinkedScene.IsConfigured,
                "An empty-string override (what TempSceneScope sets, since an unsaved scene's path is always \"\") " +
                "must make IsConfigured false even though a real value is persisted underneath it.");
        }

        [Test]
        public void IsConfiguredButNotLoaded_LinkedSceneAssetExistsButAnotherSceneIsOpen_ReturnsTrue()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            LinkAndUnload(scope, nameof(IsConfiguredButNotLoaded_LinkedSceneAssetExistsButAnotherSceneIsOpen_ReturnsTrue));

            Assert.IsTrue(TsLinkedScene.IsConfiguredButNotLoaded);
            Assert.IsFalse(TsLinkedScene.IsConfiguredButMissing,
                "The asset still genuinely exists on disk - only unloaded, not deleted.");
        }

        [Test]
        public void IsConfiguredButNotLoaded_NotConfiguredAtAll_ReturnsFalse()
        {
            Assert.IsFalse(TsLinkedScene.IsConfiguredButNotLoaded);
        }

        // B2: a linked scene asset that was genuinely deleted (not just renamed/moved, and not
        // just unloaded) needs its own distinct signal, since "open it" isn't a real recovery
        // action once the asset itself is gone - see TsWindow.DetermineLinkedSceneWarning.
        //
        // Sets SceneGuid directly to a syntactically valid GUID guaranteed not to belong to any
        // real asset, rather than deleting a real scratch scene through AssetDatabase/raw file
        // I/O and asserting on the aftermath: AssetDatabase's GUID<->path cache does not reliably
        // forget a deleted asset within the same synchronous batchmode pass (its own
        // AssetPathToGUIDOptions.IncludeRecentlyDeletedAssets option exists precisely because a
        // "recently deleted" mapping is kept alive on purpose, for Undo), which made every
        // deletion-based version of this test flaky regardless of which deletion API or how many
        // Refresh() calls were used. IsConfiguredButMissing's actual contract - a GUID on record
        // that GUIDToAssetPath cannot resolve - is what a real deletion eventually converges to
        // once Unity's own import pipeline (not a scripted Refresh()) catches up, so this tests
        // that same end-state directly and deterministically instead of racing it.
        [Test]
        public void IsConfiguredButMissing_SceneGuidDoesNotResolveToAnyAsset_ReturnsTrueAndNotJustNotLoaded()
        {
            TsLinkedScene.ClearOverride();

            TsLinkedScene.SceneGuid = "00000000000000000000000000000000";

            Assert.IsTrue(TsLinkedScene.IsConfiguredButMissing);
            Assert.IsTrue(TsLinkedScene.IsConfiguredButNotLoaded,
                "IsConfiguredButMissing must be a strict subset of IsConfiguredButNotLoaded, so every " +
                "write-guard checking the broader flag still safely blocks in this state too.");
        }

        [Test]
        public void IsConfiguredButMissing_NotConfiguredAtAll_ReturnsFalse()
        {
            Assert.IsFalse(TsLinkedScene.IsConfiguredButMissing);
        }

        [Test]
        public void IsConfiguredButMissing_ConfiguredAndLoaded_ReturnsFalse()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/MissingCheckLoaded.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);
            TsLinkedScene.ScenePath = scenePath;

            Assert.IsFalse(TsLinkedScene.IsConfiguredButMissing);
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

        // More than one match in the scoped, linked scene must never be silently resolved to
        // whichever one Unity's internal iteration order returns first.
        // TsGenerator.DetermineAmbiguousConfigWarning turns this into a loud diagnostic; this only
        // pins that the low-level lookup itself refuses to guess.
        [Test]
        public void Find_ConfiguredAndLoaded_MultipleMatches_ReturnsNull()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/FindAmbiguousTest.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);
            TsLinkedScene.ScenePath = scenePath;
            scope.CreateGameObject("A").AddComponent<TsConfig>();
            scope.CreateGameObject("B").AddComponent<TsConfig>();

            var found = TsLinkedScene.Find<TsConfig>();

            Assert.IsNull(found);
        }

        [Test]
        public void Find_ConfiguredButNotLoaded_ReturnsNullEvenIfSomethingMatchesInAnotherScene()
        {
            using var linkedScope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            LinkAndUnload(linkedScope, nameof(Find_ConfiguredButNotLoaded_ReturnsNullEvenIfSomethingMatchesInAnotherScene));
            // A different, currently-open scene has its own TsConfig - not the one linked above.
            using var otherScope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            otherScope.CreateGameObject("TsConfig").AddComponent<TsConfig>();

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
            using var linkedScope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            LinkAndUnload(linkedScope, nameof(FindType_ConfiguredButNotLoaded_ReturnsNullEvenIfSomethingMatchesInAnotherScene));
            using var otherScope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            otherScope.CreateGameObject("TsConfig").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindType(typeof(TsConfig));

            Assert.IsNull(found);
        }

        // FindAll<T>() is the "every match" twin of Find<T>(), used where a module needs more
        // than the first match (TranslationModule's TMP target scan). Same fallback/scoping.
        [Test]
        public void FindAll_NotConfigured_FallsBackToAnyLoadedScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            scope.CreateGameObject("A").AddComponent<TsConfig>();
            scope.CreateGameObject("B").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindAll<TsConfig>();

            Assert.AreEqual(2, found.Count);
        }

        [Test]
        public void FindAll_ConfiguredAndLoaded_FindsEveryMatchInThatScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/FindAllTest.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);
            TsLinkedScene.ScenePath = scenePath;
            scope.CreateGameObject("A").AddComponent<TsConfig>();
            scope.CreateGameObject("B").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindAll<TsConfig>();

            Assert.AreEqual(2, found.Count);
        }

        [Test]
        public void FindAll_ConfiguredButNotLoaded_ReturnsEmptyEvenIfSomethingMatchesInAnotherScene()
        {
            using var linkedScope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            LinkAndUnload(linkedScope, nameof(FindAll_ConfiguredButNotLoaded_ReturnsEmptyEvenIfSomethingMatchesInAnotherScene));
            using var otherScope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            otherScope.CreateGameObject("A").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindAll<TsConfig>();

            CollectionAssert.IsEmpty(found);
        }

        // FindAllType(Type) is the runtime-Type twin of FindAll<T>(), for callers that only know
        // the type reflectively (ScaffoldModule's compiled root type). Same fallback/scoping
        // contract, so only one representative case per branch is needed here rather than
        // re-proving FindAll<T>()'s full behavior a second time.
        [Test]
        public void FindAllType_NotConfigured_FallsBackToAnyLoadedScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            scope.CreateGameObject("A").AddComponent<TsConfig>();
            scope.CreateGameObject("B").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindAllType(typeof(TsConfig));

            Assert.AreEqual(2, found.Count);
        }

        [Test]
        public void FindAllType_ConfiguredAndLoaded_FindsEveryMatchInThatScene()
        {
            using var scope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            string scenePath = ScratchAssets.Folder + "/FindAllTypeTest.unity";
            EditorSceneManager.SaveScene(scope.Scene, scenePath);
            TsLinkedScene.ScenePath = scenePath;
            scope.CreateGameObject("A").AddComponent<TsConfig>();
            scope.CreateGameObject("B").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindAllType(typeof(TsConfig));

            Assert.AreEqual(2, found.Count);
        }

        [Test]
        public void FindAllType_ConfiguredButNotLoaded_ReturnsEmptyEvenIfSomethingMatchesInAnotherScene()
        {
            using var linkedScope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            LinkAndUnload(linkedScope, nameof(FindAllType_ConfiguredButNotLoaded_ReturnsEmptyEvenIfSomethingMatchesInAnotherScene));
            using var otherScope = new TempSceneScope();
            TsLinkedScene.ClearOverride();
            otherScope.CreateGameObject("A").AddComponent<TsConfig>();

            var found = TsLinkedScene.FindAllType(typeof(TsConfig));

            CollectionAssert.IsEmpty(found);
        }
    }
}
