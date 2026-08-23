using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Tests exercise the _nextTickDueAtRealTime stale-tick discard guard either by
    // reflection-setting it directly, or by simulating the exact discrete callback
    // sequence a real stale-then-restarted loop produces.
    public class ProcessUpdateLoopTests : ProcessTestBase
    {
        [Test]
        public void Tick_LoopNotActive_IsNoOpAndDoesNotTouchTheFlag()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", false);

            process._TickProcessUpdate();

            Assert.AreEqual(0, process.OnProcessUpdateCount);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }

        [Test]
        public void Tick_ActiveButNotRunning_StopsLoopWithoutCallingOnProcessUpdate()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            PrivateFieldAccess.SetField(process, "_isRunning", false);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", true);

            process._TickProcessUpdate();

            Assert.AreEqual(0, process.OnProcessUpdateCount);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }

        [Test]
        public void Tick_ActiveAndRunningButNotOwner_StopsLoopWithoutCallingOnProcessUpdate()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", 1);
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", 2);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", true);

            process._TickProcessUpdate();

            Assert.AreEqual(0, process.OnProcessUpdateCount);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }

        [Test]
        public void Tick_ActiveRunningOwner_CallsOnProcessUpdateAndStaysActive()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_useProcessUpdate", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", true);

            process._TickProcessUpdate();

            Assert.AreEqual(1, process.OnProcessUpdateCount);
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }

        [Test]
        public void Tick_OnProcessUpdateStopsProcess_LoopDoesNotStayActiveAfterCallback()
        {
            // The post-callback re-check is a distinct guard from the entry guard
            // tested above; this proves the exit check works independently.
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_useProcessUpdate", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", true);
            process.OnProcessUpdateAction = () => PrivateFieldAccess.SetField(process, "_isRunning", false);

            process._TickProcessUpdate();

            Assert.AreEqual(1, process.OnProcessUpdateCount);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }

        [Test]
        public void Tick_RepeatedDirectInvocations_SimulateContinuousLoopThenStopHaltsIt()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess(useProcessUpdate: true);

            process._TickProcessUpdate();
            ForceNextTickDueNow(process);
            process._TickProcessUpdate();
            ForceNextTickDueNow(process);
            process._TickProcessUpdate();
            Assert.AreEqual(3, process.OnProcessUpdateCount);
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));

            process.StopProcess();
            // A stale tick arriving after the loop was stopped must not tick again.
            process._TickProcessUpdate();

            Assert.AreEqual(3, process.OnProcessUpdateCount);
        }

        [Test]
        public void Tick_NextTickDueInTheFuture_IsDiscardedAsPremature_DoesNotReschedule()
        {
            // A call arriving before its recorded due time belongs to an earlier,
            // already-superseded loop generation and must be discarded without firing
            // OnProcessUpdate.
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", true);
            PrivateFieldAccess.SetField(process, "_nextTickDueAtRealTime", Time.realtimeSinceStartup + 1000f);

            process._TickProcessUpdate();

            Assert.AreEqual(0, process.OnProcessUpdateCount);
            // Still logically active - a premature call must not kill a legitimate loop.
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }

        [Test]
        public void Tick_NextTickDueNowOrInThePast_ProceedsAndAdvancesTheDeadline()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_useProcessUpdate", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", true);
            PrivateFieldAccess.SetField(process, "_nextTickDueAtRealTime", 0f);

            process._TickProcessUpdate();

            Assert.AreEqual(1, process.OnProcessUpdateCount);
            float dueAfter = PrivateFieldAccess.GetField<float>(process, "_nextTickDueAtRealTime");
            Assert.GreaterOrEqual(dueAfter, Time.realtimeSinceStartup);
        }

        // RequestSerialization() itself is an unobservable no-op stub in the Editor proxy (see
        // StartProcess_AlreadyOwner_CallsRequestSerializationBranch_NotSetProcessOwnerBranch in
        // ProcessLifecycleTests), so these pin down the resync heartbeat's scheduling decision
        // via _nextResyncDueAtRealTime rather than the resync call itself.
        [Test]
        public void Tick_ResyncNotYetDue_LeavesTheResyncDeadlineUntouched()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", true);
            float farFutureDeadline = Time.realtimeSinceStartup + 1000f;
            PrivateFieldAccess.SetField(process, "_nextResyncDueAtRealTime", farFutureDeadline);

            process._TickProcessUpdate();

            Assert.AreEqual(farFutureDeadline,
                PrivateFieldAccess.GetField<float>(process, "_nextResyncDueAtRealTime"),
                "A tick before the resync deadline must not touch it - resync runs on its own, " +
                "coarser cadence than the regular tick.");
        }

        [Test]
        public void Tick_ResyncDueNowOrInThePast_AdvancesTheResyncDeadline()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", true);
            PrivateFieldAccess.SetField(process, "_nextResyncDueAtRealTime", 0f);

            process._TickProcessUpdate();

            Assert.Greater(PrivateFieldAccess.GetField<float>(process, "_nextResyncDueAtRealTime"),
                Time.realtimeSinceStartup,
                "A due resync must reschedule itself into the future, the same way the regular " +
                "tick deadline advances - otherwise it would fire every tick from then on.");
        }

        [Test]
        public void StartProcess_UseProcessUpdateFalse_StillSchedulesAFutureResyncDeadline()
        {
            // The resync heartbeat exists to self-heal a missed discrete broadcast (see Process's
            // class remarks) for ANY running, owned process, not just ones that opted into
            // OnProcessUpdate - so it must be scheduled unconditionally by StartProcess.
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);

            process.StartProcess();

            Assert.Greater(PrivateFieldAccess.GetField<float>(process, "_nextResyncDueAtRealTime"),
                Time.realtimeSinceStartup);
        }

        [Test]
        public void StopThenStartSameFrame_StaleTickArrivingLate_IsDiscarded()
        {
            // Simulates the discrete callback sequence a same-frame Stop+Start
            // produces: old loop "A"'s legitimate first tick, the same-frame
            // Stop+Start, new loop "B"'s legitimate first tick, and finally A's
            // already-scheduled next call ("A-next") arriving late.
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess(useProcessUpdate: true); // Loop A starts.

            process._TickProcessUpdate(); // A's first (legitimate) tick fires.
            Assert.AreEqual(1, process.OnProcessUpdateCount);

            process.StopProcess(); // Same frame: A stopped...
            SeedAsOwner(process);
            process.StartProcess(useProcessUpdate: true); // ...then B starts.

            process._TickProcessUpdate(); // B's first (legitimate) tick fires.
            Assert.AreEqual(2, process.OnProcessUpdateCount);

            process._TickProcessUpdate(); // A-next finally arrives, late and stale.
            Assert.AreEqual(2, process.OnProcessUpdateCount, "Stale tick from the stopped loop must not fire.");
        }

        [Test]
        public void Start_ReentrantStopThenStartFromOnProcessStarted_LeavesTheNewLoopCorrectlyScheduled()
        {
            // OnProcessStartedAction below reentrantly stops and restarts the process,
            // exercising the one path where StartProcess's own !_updateLoopActive guard matters.
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            process.OnProcessStartedAction = () =>
            {
                process.OnProcessStartedAction = null; // avoid recursing into this same hook again
                process.StopProcess();
                SeedAsOwner(process); // StopProcess's cleanup clears the owner fields
                process.StartProcess(useProcessUpdate: true);
            };

            process.StartProcess(useProcessUpdate: true);

            Assert.IsTrue(process.IsProcessRunning());
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
            CollectionAssert.AreEqual(
                new[] { "OnProcessStarted", "OnProcessStopped", "OnProcessCleanup:False", "OnProcessStarted" },
                process.CallLog);

            process._TickProcessUpdate();
            Assert.AreEqual(1, process.OnProcessUpdateCount);
        }
    }
}
