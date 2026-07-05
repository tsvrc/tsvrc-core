using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Tsvrc.Tests.Editor
{
    // TsvrcBuildCompile ensures generated files/wiring are current before every VRChat
    // world build. No real build is needed to exercise it - just call OnBuildRequested
    // directly, per CODEGEN_TESTING_PLAN.md Phase G1.9.
    //
    // TsvrcGenerator.Run() is bootstrap-gated (HasBootstrapSignal()) and this project has
    // no TsvrcInstance subclass of its own yet, so a bare call would silently no-op via
    // WaitForBootstrapSignal rather than actually running - add a real TsvrcConfig to a
    // temp scene first to guarantee a deterministic, real bootstrap signal.
    //
    // TsvrcGenerator is a static, project-wide singleton - Run() really does write
    // Assets/TsvrcGenerated/*.cs to disk for real (WriteIfChanged is a genuine file write,
    // not scratch). Driving it from an empty synthetic scene/config could silently
    // overwrite a developer's actual configured output with "stub" content. Back up and
    // byte-for-byte restore those files around the test so this test is destructive-safe
    // regardless of what real content happens to be configured when it runs.
    public class TsvrcBuildCompileTests
    {
        private const string GeneratedFolder = "Assets/TsvrcGenerated";

        private static readonly string[] GeneratedFileNames =
        {
            "TsvrcGenerated.cs", "TsvrcGeneratedConstruct.cs", "TsvrcGeneratedFactory.cs",
            "TsvrcGeneratedInstance.cs", "TsvrcGeneratedMemory.cs", "TsvrcGeneratedPool.cs",
            "TsvrcGeneratedSingleton.cs", "TsvrcGeneratedTranslation.cs",
        };

        private TempSceneScope _scope;
        private readonly Dictionary<string, string> _backup = new Dictionary<string, string>();

        [SetUp]
        public void SetUp()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            _backup.Clear();
            foreach (var fileName in GeneratedFileNames)
            {
                string path = Path.Combine(projectRoot, GeneratedFolder.Replace('/', Path.DirectorySeparatorChar), fileName);
                if (File.Exists(path)) _backup[path] = File.ReadAllText(path);
            }

            _scope = new TempSceneScope();
            _scope.CreateGameObject("TsvrcConfig").AddComponent<TsvrcConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            foreach (var (path, content) in _backup)
                File.WriteAllText(path, content);

            // Run() leaves EditorApplication.hierarchyChanged/Undo.postprocessModifications
            // subscribed for the rest of the process once it completes past the bootstrap
            // gate - harmless in production (that's the whole design), but in a test session
            // it means those handlers would keep reacting to every subsequent test's temp
            // scene churn. Since the TsvrcConfig-bearing scope is already disposed above,
            // this call finds no bootstrap signal, unsubscribes OnHierarchyChanged (Run()
            // does that unconditionally at its own top) and re-arms only the lightweight
            // WaitForBootstrapSignal poller, restoring a safe idle state for later tests.
            TsvrcGenerator.AfterDomainReload(skipRefresh: true);
        }

        [Test]
        public void CallbackOrder_IsMinus99_SoItSettlesBeforeUdonSharpsOwnBuildPass()
        {
            Assert.AreEqual(-99, new TsvrcBuildCompile().callbackOrder);
        }

        [Test]
        public void OnBuildRequested_SceneBuild_ReturnsTrueAndRunsGeneratorPopulatingWatchedPaths()
        {
            var callback = new TsvrcBuildCompile();

            bool result = callback.OnBuildRequested(VRCSDKRequestedBuildType.Scene);

            Assert.IsTrue(result);
            // AfterDomainReload(skipRefresh:true) -> Run() populates TsvrcGenerator.WatchedPaths
            // once the bootstrap gate passes. A non-empty set is the only externally observable
            // proof the generator pass actually executed.
            Assert.IsNotEmpty(TsvrcGenerator.WatchedPaths);
        }

        [Test]
        public void OnBuildRequested_NonSceneBuild_ReturnsTrueWithoutRunningTheGenerator()
        {
            var callback = new TsvrcBuildCompile();

            // Establish a known, non-empty baseline first (Scene build above, or any prior
            // Run()), then clear it via a type we don't have a public reset for - instead,
            // capture the reference before/after and confirm it is the exact same collection
            // instance (Run() always assigns a brand-new HashSet to WatchedPaths, so an
            // unchanged reference proves Run() did not execute).
            callback.OnBuildRequested(VRCSDKRequestedBuildType.Scene);
            var before = TsvrcGenerator.WatchedPaths;

            bool result = callback.OnBuildRequested(VRCSDKRequestedBuildType.Avatar);

            Assert.IsTrue(result);
            Assert.AreSame(before, TsvrcGenerator.WatchedPaths, "A non-Scene build type must short-circuit before ever calling Run().");
        }
    }
}
