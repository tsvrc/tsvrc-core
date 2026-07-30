using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator's _isWiring and _justFinishedWiring suppression is the mechanism that
    // stops Wire()'s own SerializedObject writes from recursively triggering another
    // ScheduleRerun() via OnPostprocessModifications. This drives the real, static,
    // project-wide Run(), through TsGeneratorTestHarness's scratch folder redirect so this
    // never touches a consuming project's real Assets/TsGenerated, with a real compiled root
    // present so Wire() actually runs.
    public class TsGeneratorWiringSuppressionTests
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
        public void TearDown() => _harness.Dispose();

        [Test]
        public void Run_WithCompiledRootPresent_WiresWithoutSchedulingARecursiveRerun()
        {
            CompiledRootFixture.AddTo(_scope);

            // Run() once first to let GenerateCode() output settle to whatever this test's
            // empty config produces against the empty, freshly created scratch folder. The
            // first pass's own file writes are a legitimate, externally looking reason for
            // TsAssetWatcher to schedule a rerun, which isn't what this test is about. This
            // resets _rerunPending afterward so the real assertion below is only about the
            // second pass, where content is already stable and nothing external changes.
            TsGenerator.AfterDomainReload(skipRefresh: true);
            PrivateFieldAccess.SetField(typeof(TsGenerator), "_rerunPending", false);

            TsGenerator.AfterDomainReload(skipRefresh: true);

            // Immediately after this second Run() returns, before any EditorApplication.
            // delayCall has had a chance to fire, _isWiring must already be cleared since the
            // finally block ran, _justFinishedWiring must still be true since its own delayCall
            // hasn't fired yet, and _rerunPending must be false. This proves Wire()'s own
            // scene and SerializedObject writes did not get treated as an external
            // modification that needs a rerun.
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_isWiring"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_justFinishedWiring"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_rerunPending"));
        }
    }
}
