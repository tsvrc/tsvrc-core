using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Tsvrc.Tests.EditMode
{
    // TsBuildCompile ensures generated files and wiring are current before every VRChat world
    // build. OnBuildRequested can be called directly without a real build.
    //
    // TsGenerator.Run() is bootstrap gated and this project has no Instance subclass, so a bare
    // call would silently no-op via WaitForBootstrapSignal. This adds a real TsConfig first to
    // guarantee a deterministic bootstrap signal.
    //
    // TsGenerator is a static, project-wide singleton, and Run() writes real files. Wrapping it
    // in a TsGeneratorTestHarness redirects those writes to a scratch folder instead of a
    // consuming project's real Assets/TsGenerated. TearDown's harness Dispose() resets the
    // process-lifetime EditorApplication hooks Run() leaves subscribed.
    public class TsBuildCompileTests
    {
        private TsGeneratorTestHarness _harness;
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _harness = new TsGeneratorTestHarness();
            _scope = _harness.Scope;
            _scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();
        }

        [TearDown]
        public void TearDown() => _harness.Dispose(); // also resets the hierarchyChanged/postprocessModifications hooks, see TsGeneratorTestHarness

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
            // SetUp's TsConfig guarantees HasBootstrapSignal() is true, so OnBuildRequested
            // returns true immediately without reaching the confirm dialog. This is the only
            // branch safe to test automatically, since the "never bootstrapped" branch calls
            // EditorUtility.DisplayDialog, whose behavior under batchmode isn't reliable.
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
