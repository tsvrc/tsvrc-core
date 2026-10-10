using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // SuppressAutomaticTriggers() / AutomaticTriggersSuppressed: what stops TsGenerator's
    // reactive hierarchyChanged/asset-watcher triggers from regenerating TsGenerated against
    // whatever scene a test run happens to have open.
    //
    // AutomaticTriggersSuppressed is true for this whole suite, since a test run is in progress,
    // so the scope tests below assert relative changes in _suppressionDepth rather than an
    // absolute "currently unsuppressed" state.
    public class TsGeneratorAutomaticTriggersTests
    {
        [Test]
        public void AutomaticTriggersSuppressed_DuringATestRunWithNoScopeHeld_IsTrue()
        {
            Assume.That(PrivateFieldAccess.GetField<int>(typeof(TsGenerator), "_suppressionDepth"), Is.EqualTo(0));

            Assert.IsTrue(TsGenerator.AutomaticTriggersSuppressed,
                "A test run's scenes never hold the project's real TsConfig, so the reactive triggers " +
                "must hold back for the whole run without any scope being taken.");
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
