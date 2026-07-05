using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // TsvrcGenerator.Run(skipRefresh: true)'s control-flow subtlety (CODEGEN_TESTING_PLAN.md
    // §1.2.a / Part 4 item 3 / Phase G6.7): the `!skipRefresh` guard only gates the *early
    // return* after WriteModules() - when skipRefresh is true and files changed, execution
    // falls through to AfterFilesStable() and Wire() in the SAME call, wiring against
    // whatever compiled type was ALREADY loaded (stale, since no recompile happened). This
    // pins that current behavior: it must not throw or corrupt scene state, even though the
    // fields it's wiring against don't exist yet on the stale compiled type.
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
        public void Run_SkipRefreshTrueWithPendingFileChanges_FallsThroughToWireWithoutThrowingOrCorruptingScene()
        {
            // A real Singleton entry guarantees GenerateCode() output differs from whatever
            // is currently on disk (forcing WriteModules() to return true and actually write),
            // exercising the fallthrough path deterministically regardless of prior state.
            var configGo = _scope.CreateGameObject("TsvrcConfig");
            var config = configGo.AddComponent<TsvrcConfig>();
            var singletonTarget = _scope.CreateGameObject("__SkipRefreshFallthroughSingleton__");
            config.Singletons = new Object[] { singletonTarget };

            var root = CompiledRootFixture.AddTo(_scope);

            // The stale (already-loaded) compiled type has no field for the new Singleton
            // entry yet, so Wire() (reached via fallthrough) will warn - that's the whole
            // point: confirming it warns rather than throws or skipping straight past.
            LogAssert.ignoreFailingMessages = true;

            Assert.DoesNotThrow(() => TsvrcGenerator.AfterDomainReload(skipRefresh: true));

            LogAssert.ignoreFailingMessages = false;

            // Scene must still be exactly as constructed - fallthrough Wire() must not have
            // destroyed or duplicated the root or the config in the process of failing to
            // find fields.
            Assert.IsNotNull(root, "Root must survive the fallthrough Wire() pass.");
            Assert.IsNotNull(configGo, "Config GameObject must survive the fallthrough Wire() pass.");
            Assert.IsTrue(configGo.activeInHierarchy);
        }
    }
}
