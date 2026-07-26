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
        private const string PendingBootstrapKey = "Tsvrc.PendingBootstrap";

        private TempSceneScope _scope;
        private GeneratedFileBackup _backup;

        [SetUp]
        public void SetUp()
        {
            SessionState.EraseBool(PendingBootstrapKey);
            _backup = new GeneratedFileBackup();
            _scope = new TempSceneScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            _backup.Dispose();
            SessionState.EraseBool(PendingBootstrapKey);
            TsGenerator.AfterDomainReload(skipRefresh: true); // reset hooks, see TsBuildCompileTests
        }

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
            SessionState.SetBool(PendingBootstrapKey, true);

            TsGenerator.AfterDomainReload(skipRefresh: true);

            Assert.IsFalse(TsGenerator.IsBootstrapPending,
                "The flag must be consumed by the very next AfterDomainReload() call, whether or not " +
                "that pass itself needs to re-arm it (e.g. because it hits another write-then-stop).");
        }

        [Test]
        public void AfterDomainReload_WithNoPendingFlag_DoesNotForceBootstrap()
        {
            // No TsConfig, no scaffold instance, no TsInstance subclass in this synthetic scene -
            // HasBootstrapSignal() is false. Without a pending flag, this must stay gated (no scene
            // object should be created) rather than treating every domain reload as a bootstrap.
            Assert.IsFalse(TsGenerator.IsBootstrapPending);

            Assert.DoesNotThrow(() => TsGenerator.AfterDomainReload(skipRefresh: true));

            Assert.IsFalse(TsGenerator.IsBootstrapPending);
        }

        [Test]
        public void Run_ReachesWireWithoutFurtherWrites_LeavesIsBootstrapPendingFalse()
        {
            // Once a pass completes without needing another write (the "fully settled" case), no
            // pending flag should be left behind for a future reload to misinterpret as a bootstrap
            // request.
            var root = CompiledRootFixture.AddTo(_scope);
            if (root == null) return; // Assert.Ignore already raised inside the fixture.

            var configGo = _scope.CreateGameObject("TsConfig");
            configGo.transform.SetParent(root.transform);
            configGo.AddComponent<TsConfig>();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsFalse(TsGenerator.IsBootstrapPending,
                "A pass that reaches Wire() without writing anything new must not leave a stale pending flag.");
        }
    }
}
