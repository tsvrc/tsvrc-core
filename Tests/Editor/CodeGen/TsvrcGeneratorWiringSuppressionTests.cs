using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // TsvrcGenerator's _isWiring/_justFinishedWiring suppression - the mechanism that
    // stops Wire()'s own SerializedObject writes from recursively triggering another
    // ScheduleRerun() via OnPostprocessModifications. Drives the real, static,
    // project-wide Run() (via GeneratedFileBackup + the same TsvrcConfig-in-a-temp-scene
    // bootstrap pattern as TsvrcBuildCompileTests), with a real compiled root present so
    // Wire() actually runs.
    public class TsvrcGeneratorWiringSuppressionTests
    {
        private TempSceneScope _scope;
        private GeneratedFileBackup _backup;

        [SetUp]
        public void SetUp()
        {
            _backup = new GeneratedFileBackup();
            _scope = new TempSceneScope();
            _scope.CreateGameObject("TsvrcConfig").AddComponent<TsvrcConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            _backup.Dispose();
            TsvrcGenerator.AfterDomainReload(skipRefresh: true); // reset hooks, see TsvrcBuildCompileTests
        }

        [Test]
        public void Run_WithCompiledRootPresent_WiresWithoutSchedulingARecursiveRerun()
        {
            CompiledRootFixture.AddTo(_scope);

            // Run() once first to let GenerateCode() output settle to whatever this test's
            // (empty) config produces. If the project happened to be in a *different* state
            // beforehand (e.g. CodeGenSandbox.Bootstrap() has been run), this first pass's
            // own file-content change is a legitimate, *external-looking* reason for
            // TsvrcAssetWatcher to schedule a rerun - that's not what this test is about.
            // Reset _rerunPending afterward so the real assertion below is only about the
            // second pass, where content is already stable and nothing external changes.
            TsvrcGenerator.AfterDomainReload(skipRefresh: true);
            PrivateFieldAccess.SetField(typeof(TsvrcGenerator), "_rerunPending", false);

            TsvrcGenerator.AfterDomainReload(skipRefresh: true);

            // Immediately after this second Run() returns (before any EditorApplication.
            // delayCall has had a chance to fire): _isWiring must already be cleared (the
            // finally block ran), _justFinishedWiring must still be true (its own delayCall
            // hasn't fired yet), and _rerunPending must be false - proving Wire()'s own
            // scene/SerializedObject writes did not get treated as an external modification
            // that needs a rerun.
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsvrcGenerator), "_isWiring"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(typeof(TsvrcGenerator), "_justFinishedWiring"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsvrcGenerator), "_rerunPending"));
        }
    }
}
