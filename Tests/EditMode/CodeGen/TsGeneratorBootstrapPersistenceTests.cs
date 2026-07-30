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
            // bootstrap request. This calls Run() twice: the first pass writes GenerateCode()'s
            // output for this test's empty-config scene to the scratch folder and stops there,
            // since WriteModules() returned true. The second pass sees that same content
            // already on disk, so there is no diff, and it proceeds past the write-then-stop
            // branches into Wire(). This mirrors the same two-pass settle pattern
            // TsGeneratorWiringSuppressionTests uses, for the same reason: there is no other way
            // to reach Wire() deterministically without a real domain reload in between.
            var root = CompiledRootFixture.AddTo(_scope);
            var configGo = _scope.CreateGameObject("TsConfig");
            configGo.transform.SetParent(root.transform);
            configGo.AddComponent<TsConfig>();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsFalse(TsGenerator.IsBootstrapPending,
                "A pass that reaches Wire() without writing anything new must not leave a stale pending flag.");
        }
    }
}
