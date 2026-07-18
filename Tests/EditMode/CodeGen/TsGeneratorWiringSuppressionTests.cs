using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator's _isWiring/_justFinishedWiring suppression - the mechanism that
    // stops Wire()'s own SerializedObject writes from recursively triggering another
    // ScheduleRerun() via OnPostprocessModifications. Drives the real, static,
    // project-wide Run() (via GeneratedFileBackup + the same TsConfig-in-a-temp-scene
    // bootstrap pattern as TsBuildCompileTests), with a real compiled root present so
    // Wire() actually runs.
    public class TsGeneratorWiringSuppressionTests
    {
        private TempSceneScope _scope;
        private GeneratedFileBackup _backup;

        [SetUp]
        public void SetUp()
        {
            _backup = new GeneratedFileBackup();
            _scope = new TempSceneScope();
            _scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            _backup.Dispose();
            TsGenerator.AfterDomainReload(skipRefresh: true); // reset hooks, see TsBuildCompileTests
        }

        [Test]
        public void Run_WithCompiledRootPresent_WiresWithoutSchedulingARecursiveRerun()
        {
            CompiledRootFixture.AddTo(_scope);

            // Run() once first to let GenerateCode() output settle to whatever this test's
            // (empty) config produces. If the project's generated files happened to already
            // differ from that, this first pass's own file-content change is a legitimate,
            // *external-looking* reason for TsAssetWatcher to schedule a rerun - that's not
            // what this test is about. Reset _rerunPending afterward so the real assertion
            // below is only about the second pass, where content is already stable and
            // nothing external changes.
            TsGenerator.AfterDomainReload(skipRefresh: true);
            PrivateFieldAccess.SetField(typeof(TsGenerator), "_rerunPending", false);

            TsGenerator.AfterDomainReload(skipRefresh: true);

            // Immediately after this second Run() returns (before any EditorApplication.
            // delayCall has had a chance to fire): _isWiring must already be cleared (the
            // finally block ran), _justFinishedWiring must still be true (its own delayCall
            // hasn't fired yet), and _rerunPending must be false - proving Wire()'s own
            // scene/SerializedObject writes did not get treated as an external modification
            // that needs a rerun.
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_isWiring"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_justFinishedWiring"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_rerunPending"));
        }
    }
}
