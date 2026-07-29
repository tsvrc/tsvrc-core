using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Tsvrc.Tests.EditMode
{
    // TsBuildCompile ensures generated files/wiring are current before every VRChat world
    // build; OnBuildRequested can be called directly without a real build.
    //
    // TsGenerator.Run() is bootstrap-gated and this project has no Instance subclass, so a
    // bare call would silently no-op via WaitForBootstrapSignal - add a real TsConfig first to
    // guarantee a deterministic bootstrap signal.
    //
    // TsGenerator is a static, project-wide singleton - Run() writes Assets/TsGenerated/*.cs for
    // real. GeneratedFileBackup makes that safe; TearDown's extra AfterDomainReload() resets the
    // process-lifetime EditorApplication hooks Run() leaves subscribed.
    public class TsBuildCompileTests
    {
        private TempSceneScope _scope;
        private GeneratedFileBackup _backup;

        [SetUp]
        public void SetUp()
        {
            _backup = new GeneratedFileBackup();
            _scope = new TempSceneScope();
            _scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            _backup.Dispose();

            // Run() leaves EditorApplication.hierarchyChanged/Undo.postprocessModifications
            // subscribed process-wide once past the bootstrap gate. With the TsConfig-bearing
            // scope already disposed above, this call finds no bootstrap signal, unsubscribes
            // OnHierarchyChanged, and re-arms only the WaitForBootstrapSignal poller - restoring
            // a safe idle state for later tests.
            TsGenerator.AfterDomainReload(skipRefresh: true);
        }

        [Test]
        public void CallbackOrder_IsMinus99_SoItSettlesBeforeUdonSharpsOwnBuildPass()
        {
            Assert.AreEqual(-99, new TsBuildCompile().callbackOrder);
        }

        [Test]
        public void OnBuildRequested_SceneBuild_ReturnsTrueAndRunsGeneratorPopulatingWatchedPaths()
        {
            var callback = new TsBuildCompile();

            bool result = callback.OnBuildRequested(VRCSDKRequestedBuildType.Scene);

            Assert.IsTrue(result);
            // Run() populates TsGenerator.WatchedPaths once the bootstrap gate passes; a non-empty
            // set is the only externally observable proof the generator pass actually executed.
            Assert.IsNotEmpty(TsGenerator.WatchedPaths);
        }

        [Test]
        public void OnBuildRequested_SceneBuildAlreadyBootstrapped_ReturnsTrueWithoutShowingDialog()
        {
            // SetUp's TsConfig guarantees HasBootstrapSignal() is true, so OnBuildRequested returns
            // true immediately without reaching the confirm dialog - the only branch safe to test
            // automatedly, since the "never bootstrapped" branch calls EditorUtility.DisplayDialog,
            // whose behavior under -batchmode isn't reliable.
            var callback = new TsBuildCompile();

            bool result = callback.OnBuildRequested(VRCSDKRequestedBuildType.Scene);

            Assert.IsTrue(result);
        }

        [Test]
        public void OnBuildRequested_NonSceneBuild_ReturnsTrueWithoutRunningTheGenerator()
        {
            var callback = new TsBuildCompile();

            // Establish a known, non-empty baseline (Scene build above), then capture the
            // WatchedPaths reference before/after: Run() always assigns a brand-new HashSet,
            // so an unchanged reference proves Run() did not execute.
            callback.OnBuildRequested(VRCSDKRequestedBuildType.Scene);
            var before = TsGenerator.WatchedPaths;

            bool result = callback.OnBuildRequested(VRCSDKRequestedBuildType.Avatar);

            Assert.IsTrue(result);
            Assert.AreSame(before, TsGenerator.WatchedPaths, "A non-Scene build type must short-circuit before ever calling Run().");
        }
    }
}
