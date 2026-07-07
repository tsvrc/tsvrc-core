using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // TsvrcGenerator.Run(skipRefresh: true)'s control-flow subtlety (CODEGEN_TESTING_PLAN.md
    // §1.2.a / Part 4 item 3 / Phase G6.7). Fixed: previously, when skipRefresh was true and
    // files changed, execution fell through to Wire() in the SAME call, wiring against
    // whatever compiled type was ALREADY loaded (stale, since no recompile happened yet).
    // Now both write-check branches return immediately whenever files were written/stable
    // changed, regardless of skipRefresh - only the AssetDatabase.Refresh() call itself is
    // skipped. Wiring is deferred to the next domain-reload-triggered Run() (see
    // TsvrcDomainReloadHandler), which runs with the now-current compiled type.
    public class TsvrcGeneratorSkipRefreshFallthroughTests
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
            TsvrcGenerator.AfterDomainReload(skipRefresh: true); // reset hooks, see TsvrcBuildCompileTests
        }

        [Test]
        public void Run_SkipRefreshTrueWithPendingFileChanges_StopsAfterWritingWithoutWiringOrThrowing()
        {
            // A real Singleton entry guarantees GenerateCode() output differs from whatever
            // is currently on disk (forcing WriteModules() to return true and actually write),
            // exercising the write-then-stop path deterministically regardless of prior state.
            var configGo = _scope.CreateGameObject("TsvrcConfig");
            var config = configGo.AddComponent<TsvrcConfig>();
            var singletonTarget = _scope.CreateGameObject("__SkipRefreshFallthroughSingleton__");
            config.Singletons = new Object[] { singletonTarget };

            var root = CompiledRootFixture.AddTo(_scope);

            // Wire() must NOT run this pass - if it did (the old, buggy fallthrough), it would
            // warn about the new Singleton entry's field missing on the stale compiled type.
            // No such warning should appear now that Run() stops before ever reaching Wire().
            LogAssert.NoUnexpectedReceived();

            Assert.DoesNotThrow(() => TsvrcGenerator.AfterDomainReload(skipRefresh: true));

            LogAssert.NoUnexpectedReceived();

            // Scene must still be exactly as constructed - stopping early must not have
            // touched the root or the config.
            Assert.IsNotNull(root, "Root must survive the write-then-stop pass.");
            Assert.IsNotNull(configGo, "Config GameObject must survive the write-then-stop pass.");
            Assert.IsTrue(configGo.activeInHierarchy);
        }
    }
}
