using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.Run(skipRefresh: true) has a control flow subtlety: both write-check
    // branches return immediately whenever files were written or something stable changed,
    // regardless of skipRefresh. Only the AssetDatabase.Refresh() call itself is skipped. This
    // avoids wiring against a stale compiled type that hasn't been recompiled from the
    // just-written source yet. Wiring is deferred to the next domain-reload-triggered Run(),
    // called from TsDomainReloadHandler, which runs with the now current compiled type.
    public class TsGeneratorSkipRefreshFallthroughTests
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
        public void Run_SkipRefreshTrueWithPendingFileChanges_StopsAfterWritingWithoutWiringOrThrowing()
        {
            // A real Global entry guarantees GenerateCode() output differs from whatever
            // is currently on disk (forcing WriteModules() to return true and actually write),
            // exercising the write-then-stop path deterministically regardless of prior state.
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var globalTarget = _scope.CreateGameObject("__SkipRefreshFallthroughGlobal__");
            config.GlobalEntries = new[] { new TsGroupedEntry { Value = globalTarget, GroupId = 0 } };

            var root = CompiledRootFixture.AddTo(_scope);

            // Wire() must not run this pass. If it did, it would warn about the new
            // Global entry's field missing on the stale compiled type. No such warning
            // should appear, since Run() stops before ever reaching Wire().
            LogAssert.NoUnexpectedReceived();

            Assert.DoesNotThrow(() => TsGenerator.AfterDomainReload(skipRefresh: true));

            LogAssert.NoUnexpectedReceived();

            // Scene must still be exactly as constructed. Stopping early must not have
            // touched the root or the config.
            Assert.IsNotNull(root, "Root must survive the write-then-stop pass.");
            Assert.IsNotNull(configGo, "Config GameObject must survive the write-then-stop pass.");
            Assert.IsTrue(configGo.activeInHierarchy);
        }
    }
}
