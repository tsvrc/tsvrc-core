using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.IsRegeneratePending: unlike IsBootstrapPending, this arms on any pass that
    // writes changed .cs content and is waiting for the recompile it caused to settle -
    // including a TsPendingConfigEdit.Apply() (allowBootstrap: false). TsWindow uses this to
    // keep further edits blocked until a regenerate triggered by a prior Apply actually finishes,
    // so an edit made in that window is never silently swept into its recompile continuation.
    public class TsGeneratorRegeneratePersistenceTests
    {
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
        public void Run_AllowBootstrapFalseWithPendingFileChanges_SetsIsRegeneratePending()
        {
            // The Apply-triggered case: ScheduleRerun()'s eventual Run() always passes
            // allowBootstrap: false, but a real file write here still means a recompile is
            // coming that further edits must be held back from.
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var globalTarget = _scope.CreateGameObject("__RegeneratePersistenceGlobal__");
            config.GlobalEntries = new[] { new TsGroupedEntry { Value = globalTarget, GroupId = 0 } };

            Assert.IsFalse(TsGenerator.IsRegeneratePending, "Must start clear.");

            TsGenerator.Run(skipRefresh: true, allowBootstrap: false);

            Assert.IsTrue(TsGenerator.IsRegeneratePending,
                "A real file write must arm IsRegeneratePending regardless of allowBootstrap.");
        }

        [Test]
        public void Run_AllowBootstrapTrueWithPendingFileChanges_AlsoSetsIsRegeneratePending()
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            var globalTarget = _scope.CreateGameObject("__RegeneratePersistenceGlobal2__");
            config.GlobalEntries = new[] { new TsGroupedEntry { Value = globalTarget, GroupId = 0 } };

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsTrue(TsGenerator.IsRegeneratePending);
        }

        [Test]
        public void AfterDomainReload_ReachesFullSettlement_ClearsIsRegeneratePending()
        {
            CompiledRootFixture.AddTo(_scope);
            TsPaths.ScaffoldScriptPath = TestGeneratedScriptPath;
            var root = _scope.CreateGameObject("TsConfig");
            root.AddComponent<TsConfig>();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            Assert.IsTrue(TsGenerator.IsRegeneratePending, "Sanity check: the first pass must write and arm the flag.");

            TsGenerator.AfterDomainReload(skipRefresh: true);

            Assert.IsFalse(TsGenerator.IsRegeneratePending,
                "A pass that reaches full settlement must clear IsRegeneratePending, or further " +
                "edits would stay blocked forever after the recompile it was waiting for finishes.");
        }

        [Test]
        public void Run_StableChangedOnlyNoFileContentChange_DoesNotSetIsRegeneratePending()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            TsPaths.ScaffoldScriptPath = TestGeneratedScriptPath;
            var configGo = _scope.CreateGameObject("TsConfig");
            configGo.transform.SetParent(root.transform);
            configGo.AddComponent<TsConfig>();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            SessionState.SetBool("Tsvrc.PendingRegenerate", false); // simulate the reload that would have cleared it

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsFalse(TsGenerator.IsRegeneratePending,
                "A stableChanged-only pass (no .cs content change) never triggers a recompile, so " +
                "it must not block further edits either.");
        }

        [Test]
        public void Run_ReachesFullySettledPathWithStaleFlag_SelfClearsWithoutAfterDomainReload()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            TsPaths.ScaffoldScriptPath = TestGeneratedScriptPath;
            var configGo = _scope.CreateGameObject("TsConfig");
            configGo.transform.SetParent(root.transform);
            configGo.AddComponent<TsConfig>();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            SessionState.SetBool("Tsvrc.PendingRegenerate", true);
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsFalse(TsGenerator.IsRegeneratePending,
                "A pass that reaches full settlement must self-clear a stale flag, not only AfterDomainReload().");
        }
    }
}
