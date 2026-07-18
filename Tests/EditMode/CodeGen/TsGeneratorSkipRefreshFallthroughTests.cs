using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.Run(skipRefresh: true)'s control-flow subtlety: both write-check
    // branches return immediately whenever files were written/stable changed, regardless
    // of skipRefresh - only the AssetDatabase.Refresh() call itself is skipped. This
    // avoids wiring against a stale compiled type that hasn't been recompiled from the
    // just-written source yet. Wiring is deferred to the next domain-reload-triggered
    // Run() (see TsDomainReloadHandler), which runs with the now-current compiled type.
    public class TsGeneratorSkipRefreshFallthroughTests
    {
        private TempSceneScope _scope;
        private GeneratedFileBackup _backup;

        [SetUp]
        public void SetUp()
        {
            _backup = new GeneratedFileBackup();
            _scope = new TempSceneScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            _backup.Dispose();
            TsGenerator.AfterDomainReload(skipRefresh: true); // reset hooks, see TsBuildCompileTests
        }

        [Test]
        public void Run_SkipRefreshTrueWithPendingFileChanges_StopsAfterWritingWithoutWiringOrThrowing()
        {
            // A real Singleton entry guarantees GenerateCode() output differs from whatever
            // is currently on disk (forcing WriteModules() to return true and actually write),
            // exercising the write-then-stop path deterministically regardless of prior state.
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var singletonTarget = _scope.CreateGameObject("__SkipRefreshFallthroughSingleton__");
            config.Singletons = new Object[] { singletonTarget };

            var root = CompiledRootFixture.AddTo(_scope);

            // Wire() must NOT run this pass - if it did, it would warn about the new
            // Singleton entry's field missing on the stale compiled type. No such warning
            // should appear, since Run() stops before ever reaching Wire().
            LogAssert.NoUnexpectedReceived();

            Assert.DoesNotThrow(() => TsGenerator.AfterDomainReload(skipRefresh: true));

            LogAssert.NoUnexpectedReceived();

            // Scene must still be exactly as constructed - stopping early must not have
            // touched the root or the config.
            Assert.IsNotNull(root, "Root must survive the write-then-stop pass.");
            Assert.IsNotNull(configGo, "Config GameObject must survive the write-then-stop pass.");
            Assert.IsTrue(configGo.activeInHierarchy);
        }
    }
}
