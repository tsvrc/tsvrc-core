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
        // Same real, permanent script location ScaffoldModuleWireTests points at: without it,
        // ScaffoldModule.AfterFilesStable() can never find a real MonoScript for its own
        // program asset (WriteModules()'s raw File.WriteAllText output is never imported while
        // skipRefresh is true), so programAssetMissing stays true forever and Wire() is never
        // reached no matter how many settle passes run.
        private const string TestGeneratedScriptPath = "Assets/Tsvrc/Tests/TestDoubles/CodeGen/TestGenerated.cs";

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
            TsPaths.ScaffoldScriptPath = TestGeneratedScriptPath;

            // Run() repeatedly first to let everything settle to whatever this test's empty
            // config produces against the empty, freshly created scratch folder: the first pass
            // writes GenerateCode()'s output and stops there; the second still has real,
            // one-time scene setup left to do (LogModule and MemoryModule creating their
            // TsLogger/TsMemory children, ScaffoldModule creating its own TsConfig child and
            // program asset), which itself counts as a change and stops the pass again; the third
            // is the first pass that reaches Wire(), and since this consuming project has a real,
            // live Instance subclass (MolInstance), InstanceModule.Wire() creates its "Instance"
            // child and program asset for the first time here too. Every priming pass' own
            // file/scene writes are a legitimate, externally looking reason for TsAssetWatcher to
            // schedule a rerun, which isn't what this test is about, so _rerunPending is reset
            // after each. Only the fourth pass has nothing left to write, create, or wire for the
            // first time, and is the one the assertions below are actually about.
            TsGenerator.AfterDomainReload(skipRefresh: true);
            PrivateFieldAccess.SetField(typeof(TsGenerator), "_rerunPending", false);

            TsGenerator.AfterDomainReload(skipRefresh: true);
            PrivateFieldAccess.SetField(typeof(TsGenerator), "_rerunPending", false);

            TsGenerator.AfterDomainReload(skipRefresh: true);
            PrivateFieldAccess.SetField(typeof(TsGenerator), "_rerunPending", false);

            TsGenerator.AfterDomainReload(skipRefresh: true);

            // Immediately after this third pass returns, before any EditorApplication.delayCall
            // has had a chance to fire, _isWiring must already be cleared since the finally block
            // ran, _justFinishedWiring must still be true since its own delayCall hasn't fired
            // yet, and _rerunPending must be false. This proves Wire()'s own scene and
            // SerializedObject writes did not get treated as an external modification that needs
            // a rerun.
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_isWiring"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_justFinishedWiring"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_rerunPending"));
        }
    }
}
