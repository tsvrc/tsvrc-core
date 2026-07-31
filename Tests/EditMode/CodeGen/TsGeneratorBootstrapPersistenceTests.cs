using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.IsBootstrapPending / the SessionState flag threaded through Run() and
    // AfterDomainReload(): a bootstrap that stops for a recompile records that fact, so the next
    // AfterDomainReload() carries it through instead of gating on HasBootstrapSignal().
    public class TsGeneratorBootstrapPersistenceTests
    {
        // Same real, permanent script location ScaffoldModuleWireTests points at, needed
        // wherever a test must reach a genuinely settled AfterFilesStable() pass: without a
        // real MonoScript on disk at ScaffoldFilePath, EnsureUdonSharpProgramAsset can never
        // create the program asset, so ScaffoldModule.AfterFilesStable() reports
        // programAssetMissing == true forever, which alone forces every Run() pass to take the
        // write-then-stop branch and never actually reach Wire().
        private const string TestGeneratedScriptPath = "Assets/Tsvrc/Tests/TestDoubles/CodeGen/TestGenerated.cs";

        private TsGeneratorTestHarness _harness;
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _harness = new TsGeneratorTestHarness();
            _scope = _harness.Scope;
        }

        [TearDown]
        public void TearDown() => _harness.Dispose();

        [Test]
        public void Run_AllowBootstrapTrueWithPendingFileChanges_SetsIsBootstrapPending()
        {
            // A real Singleton entry guarantees GenerateCode() output differs from disk, forcing
            // WriteModules() to return true and take the write-then-stop branch deterministically.
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var singletonTarget = _scope.CreateGameObject("__BootstrapPersistenceSingleton__");
            config.Singletons = new Object[] { singletonTarget };

            Assert.IsFalse(TsGenerator.IsBootstrapPending, "Must start clear.");

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsTrue(TsGenerator.IsBootstrapPending,
                "A deliberate bootstrap that had to stop for a recompile must leave a pending flag " +
                "for the next automatic pass to pick up.");
        }

        [Test]
        public void Run_AllowBootstrapFalseWithPendingFileChanges_DoesNotSetIsBootstrapPending()
        {
            // Automatic triggers (domain reload, asset watcher) pass allowBootstrap: false. Even if
            // they do reach a write (e.g. maintaining an already-bootstrapped project), that is not a
            // deliberate bootstrap click and must not arm the pending flag.
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var singletonTarget = _scope.CreateGameObject("__BootstrapPersistenceSingleton2__");
            config.Singletons = new Object[] { singletonTarget };

            TsGenerator.Run(skipRefresh: true, allowBootstrap: false);

            Assert.IsFalse(TsGenerator.IsBootstrapPending);
        }

        [Test]
        public void AfterDomainReload_WithPendingFlag_ConsumesAndClearsItBeforeRunning()
        {
            // A real compiled root and a real MonoScript at ScaffoldFilePath are both required
            // to actually reach a settled state (see TestGeneratedScriptPath's own doc comment);
            // otherwise the program asset can never be created and every pass keeps re-arming
            // the flag via the write-then-stop branch, regardless of what's under test here.
            CompiledRootFixture.AddTo(_scope);
            TsPaths.ScaffoldScriptPath = TestGeneratedScriptPath;

            // Prime the scratch folder first (same two-pass settle pattern
            // Run_ReachesWireWithoutFurtherWrites_LeavesIsBootstrapPendingFalse uses): a totally
            // empty scratch folder always has a write on its very first pass (nothing on disk
            // yet to compare against), which would legitimately re-arm the flag regardless of
            // what's under test here. Settling first isolates "was the incoming flag consumed"
            // from "did this exact pass also need to write something."
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            SessionState.SetBool("Tsvrc.PendingBootstrap", true);

            TsGenerator.AfterDomainReload(skipRefresh: true);

            Assert.IsFalse(TsGenerator.IsBootstrapPending,
                "The flag must be consumed by the very next AfterDomainReload() call, and this pass " +
                "(against an already-settled scratch folder) has nothing left to write that would " +
                "legitimately re-arm it.");
        }

        [Test]
        public void AfterDomainReload_WithNoPendingFlag_DoesNotForceBootstrap()
        {
            // No TsConfig, no scaffold instance, and no Instance subclass in this synthetic
            // scene, so HasBootstrapSignal() is false. Without a pending flag, this must stay
            // gated, with no scene object created, rather than treating every domain reload as
            // a bootstrap.
            Assert.IsFalse(TsGenerator.IsBootstrapPending);

            Assert.DoesNotThrow(() => TsGenerator.AfterDomainReload(skipRefresh: true));

            Assert.IsFalse(TsGenerator.IsBootstrapPending);
        }

        [Test]
        public void Run_ReachesWireWithoutFurtherWrites_LeavesIsBootstrapPendingFalse()
        {
            // Once a pass completes without needing another write, the fully settled case, no
            // pending flag should be left behind for a future reload to misinterpret as a
            // bootstrap request. Reaching that state from a cold scratch folder takes two Run()
            // passes: the first writes GenerateCode()'s output for this test's empty-config scene
            // and stops there, since WriteModules() returned true. The second sees that same
            // content already on disk, so WriteModules() returns false, but AfterFilesStable()
            // still has real, one-time scene setup left to do (LogModule and MemoryModule
            // creating their TsLogger/TsMemory children, ScaffoldModule creating its program
            // asset), which itself counts as a change and stops the pass again, re-arming the
            // flag. Plain Run() never clears the flag on its own, only AfterDomainReload() does
            // (it reads and clears it, then calls Run() with that value), so the actual pass
            // under test here must go through AfterDomainReload(), exactly like a real automatic
            // trigger would, not another direct Run() call.
            var root = CompiledRootFixture.AddTo(_scope);
            TsPaths.ScaffoldScriptPath = TestGeneratedScriptPath;
            var configGo = _scope.CreateGameObject("TsConfig");
            configGo.transform.SetParent(root.transform);
            configGo.AddComponent<TsConfig>();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            SessionState.SetBool("Tsvrc.PendingBootstrap", true);

            TsGenerator.AfterDomainReload(skipRefresh: true);

            Assert.IsFalse(TsGenerator.IsBootstrapPending,
                "A pass that reaches Wire() without writing anything new must not leave a stale pending flag.");
        }
    }
}
