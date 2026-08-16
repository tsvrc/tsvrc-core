using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.ComputeIsAutomatedTestProcess() / SuppressAutomaticTriggers() /
    // AutomaticTriggersSuppressed: the single mechanism that stops TsGenerator's reactive
    // hierarchyChanged/asset-watcher triggers from regenerating TsGenerated against whatever
    // scene a test run happens to have open. See TsDomainReloadHandler and the two
    // AutomaticTriggersSetUpFixture classes (Tests/EditMode, Tests/PlayMode) for how
    // this is actually armed for a real test run - the tests below exercise the primitives
    // directly instead.
    //
    // AutomaticTriggersSuppressed is not directly assertable as false in this suite: this very
    // assembly's own AutomaticTriggersSetUpFixture holds a permanent scope for the
    // whole run, and under a real -runTests batchmode invocation ComputeIsAutomatedTestProcess
    // would independently make it true anyway. Every test below is written to hold regardless of
    // that baseline, asserting relative changes in _suppressionDepth rather than an absolute
    // "currently unsuppressed" state.
    public class TsGeneratorAutomaticTriggersTests
    {
        [Test]
        public void ComputeIsAutomatedTestProcess_RunTestsFlagPresent_ReturnsTrue()
        {
            Assert.IsTrue(TsGenerator.ComputeIsAutomatedTestProcess(
                new[] { "Unity.exe", "-batchmode", "-runTests", "-testPlatform", "EditMode" }));
        }

        [Test]
        public void ComputeIsAutomatedTestProcess_RunTestsFlagPresent_IsCaseInsensitive()
        {
            Assert.IsTrue(TsGenerator.ComputeIsAutomatedTestProcess(new[] { "-RUNTESTS" }));
        }

        [Test]
        public void ComputeIsAutomatedTestProcess_NoRunTestsFlag_ReturnsFalse()
        {
            Assert.IsFalse(TsGenerator.ComputeIsAutomatedTestProcess(
                new[] { "Unity.exe", "-batchmode", "-quit" }));
        }

        [Test]
        public void ComputeIsAutomatedTestProcess_EmptyArgs_ReturnsFalse()
        {
            Assert.IsFalse(TsGenerator.ComputeIsAutomatedTestProcess(new string[0]));
        }

        [Test]
        public void SuppressAutomaticTriggers_NestedScopes_OnlyLiftsAfterEveryScopeDisposed()
        {
            int before = PrivateFieldAccess.GetField<int>(typeof(TsGenerator), "_suppressionDepth");

            var outer = TsGenerator.SuppressAutomaticTriggers();
            Assert.AreEqual(before + 1, PrivateFieldAccess.GetField<int>(typeof(TsGenerator), "_suppressionDepth"));

            var inner = TsGenerator.SuppressAutomaticTriggers();
            Assert.AreEqual(before + 2, PrivateFieldAccess.GetField<int>(typeof(TsGenerator), "_suppressionDepth"));

            inner.Dispose();
            Assert.AreEqual(before + 1, PrivateFieldAccess.GetField<int>(typeof(TsGenerator), "_suppressionDepth"),
                "Disposing the inner scope must not lift suppression while the outer scope is still held.");

            outer.Dispose();
            Assert.AreEqual(before, PrivateFieldAccess.GetField<int>(typeof(TsGenerator), "_suppressionDepth"));
        }

        [Test]
        public void SuppressAutomaticTriggers_DisposedTwice_OnlyDecrementsOnce()
        {
            int before = PrivateFieldAccess.GetField<int>(typeof(TsGenerator), "_suppressionDepth");

            var scope = TsGenerator.SuppressAutomaticTriggers();
            scope.Dispose();
            scope.Dispose();

            Assert.AreEqual(before, PrivateFieldAccess.GetField<int>(typeof(TsGenerator), "_suppressionDepth"),
                "A repeated Dispose() call must be a no-op, not decrement past what was actually acquired.");
        }

        [Test]
        public void ScheduleRerun_WhileSuppressed_LeavesRerunPendingFalse()
        {
            PrivateFieldAccess.SetField(typeof(TsGenerator), "_rerunPending", false);

            using var scope = TsGenerator.SuppressAutomaticTriggers();
            TsGenerator.ScheduleRerun();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(typeof(TsGenerator), "_rerunPending"),
                "ScheduleRerun() must not arm a real, delayed Run() pass while suppressed - that pass would " +
                "run against whatever scene is active once the delayCall fires, not necessarily this test's.");
        }
    }
}
