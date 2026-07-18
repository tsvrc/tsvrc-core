using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers OnDeserialization's running/paused-state diffing that infers lifecycle
    // events on non-owner clients, driven by directly setting the private observation
    // fields (_hasObservedState/_lastObservedIsRunning/_lastObservedIsPaused/
    // _lastObservedRunId) and the synced fields they're diffed against, then calling
    // OnDeserialization() once. The two coalesced-restart tests at the bottom are the
    // only scenarios that genuinely need two independent instances (see their own
    // comment) rather than one instance with hand-set fields.
    public class TsTimerDeserializationTests : TsTimerTestBase
    {
        private static void SeedObservedState(TsTimerTestSubclass timer, bool hasObserved, bool wasRunning, bool wasPaused, int lastObservedRunId)
        {
            PrivateFieldAccess.SetField(timer, "_hasObservedState", hasObserved);
            PrivateFieldAccess.SetField(timer, "_lastObservedIsRunning", wasRunning);
            PrivateFieldAccess.SetField(timer, "_lastObservedIsPaused", wasPaused);
            PrivateFieldAccess.SetField(timer, "_lastObservedRunId", lastObservedRunId);
        }

        [Test]
        public void OnDeserialization_LateJoinerMidRun_NotPaused_FiresOnTimerStartedOnly()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            SetIsPausedField(timer, false);
            SetRunIdField(timer, 1);
            // Defaults already model a fresh late joiner: _hasObservedState=false,
            // _lastObservedIsRunning=false.

            timer.OnDeserialization();

            Assert.AreEqual(1, timer.OnTimerStartedCount);
            Assert.AreEqual(0, timer.OnTimerPausedCount);
            Assert.AreEqual(0, timer.OnTimerStoppedCount);
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
        }

        [Test]
        public void OnDeserialization_LateJoinerMidRun_Paused_FiresOnTimerStartedAndPaused()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            SetIsPausedField(timer, true);
            SetRunIdField(timer, 1);

            timer.OnDeserialization();

            Assert.AreEqual(1, timer.OnTimerStartedCount);
            Assert.AreEqual(1, timer.OnTimerPausedCount);
        }

        [Test]
        public void OnDeserialization_FirstEverPacketWhileIdle_NoLifecycleEventJustDeserializationEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            // _isRunning stays false (default) - nothing has ever happened yet.

            timer.OnDeserialization();

            Assert.AreEqual(0, timer.OnTimerStartedCount);
            Assert.AreEqual(0, timer.OnTimerStoppedCount);
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
            Assert.AreEqual(0, timer.OnTimerPausedCount);
            Assert.AreEqual(0, timer.OnTimerResumedCount);
            Assert.AreEqual(1, timer.OnTimerDeserializationCount, "OnTimerDeserialization fires unconditionally on every call.");
        }

        [Test]
        public void OnDeserialization_HadObservedFalseGuard_StaleAlreadyIdlePacketFiresNoSpuriousStop()
        {
            // A corrupted/hand-set "was running" value that was never actually legitimately
            // observed (hadObserved=false) must not be trusted to produce a stop/complete
            // event just because the current packet also happens to show not-running.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            SeedObservedState(timer, hasObserved: false, wasRunning: true, wasPaused: false, lastObservedRunId: 1);
            PrivateFieldAccess.SetField(timer, "_isRunning", false);

            timer.OnDeserialization();

            Assert.AreEqual(0, timer.OnTimerStoppedCount);
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
        }

        [Test]
        public void OnDeserialization_NormalStopTransition_FiresOnTimerStopped()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            SeedObservedState(timer, hasObserved: true, wasRunning: true, wasPaused: false, lastObservedRunId: 1);
            PrivateFieldAccess.SetField(timer, "_isRunning", false);
            SetRunIdField(timer, 1);
            SetWasCompletedField(timer, false);

            timer.OnDeserialization();

            Assert.AreEqual(1, timer.OnTimerStoppedCount);
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
        }

        [Test]
        public void OnDeserialization_NormalCompleteTransition_FiresOnTimerCompleted()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            SeedObservedState(timer, hasObserved: true, wasRunning: true, wasPaused: false, lastObservedRunId: 1);
            PrivateFieldAccess.SetField(timer, "_isRunning", false);
            SetRunIdField(timer, 1);
            SetWasCompletedField(timer, true);

            timer.OnDeserialization();

            Assert.AreEqual(0, timer.OnTimerStoppedCount);
            Assert.AreEqual(1, timer.OnTimerCompletedCount);
        }

        [Test]
        public void OnDeserialization_PauseDetectedMidRun_FiresOnTimerPausedOnly()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            SeedObservedState(timer, hasObserved: true, wasRunning: true, wasPaused: false, lastObservedRunId: 1);
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            SetIsPausedField(timer, true);
            SetRunIdField(timer, 1);

            timer.OnDeserialization();

            Assert.AreEqual(1, timer.OnTimerPausedCount);
            Assert.AreEqual(0, timer.OnTimerResumedCount);
            Assert.AreEqual(0, timer.OnTimerStartedCount, "Still the same run - only a pause edge, not a (re)start.");
        }

        [Test]
        public void OnDeserialization_ResumeDetectedMidRun_FiresOnTimerResumedOnly()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            SeedObservedState(timer, hasObserved: true, wasRunning: true, wasPaused: true, lastObservedRunId: 1);
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            SetIsPausedField(timer, false);
            SetRunIdField(timer, 1);

            timer.OnDeserialization();

            Assert.AreEqual(1, timer.OnTimerResumedCount);
            Assert.AreEqual(0, timer.OnTimerPausedCount);
        }

        [Test]
        public void OnDeserialization_RedundantPacketNothingChanged_FiresNoLifecycleEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            SeedObservedState(timer, hasObserved: true, wasRunning: true, wasPaused: false, lastObservedRunId: 1);
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            SetIsPausedField(timer, false);
            SetRunIdField(timer, 1);

            timer.OnDeserialization();

            Assert.AreEqual(0, timer.OnTimerStartedCount);
            Assert.AreEqual(0, timer.OnTimerStoppedCount);
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
            Assert.AreEqual(0, timer.OnTimerPausedCount);
            Assert.AreEqual(0, timer.OnTimerResumedCount);
            Assert.AreEqual(1, timer.OnTimerDeserializationCount);
        }

        [Test]
        public void OnDeserialization_EventReceived_FiresAfterHookForEveryTransition()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.TsSubscribe(timer, Tsvrc.Timing.TsTimer.OnTimerDeserializationEvent, nameof(timer._OnTimerDeserializationEventReceived));

            timer.OnDeserialization();

            CollectionAssert.AreEqual(new[] { "Hook:OnTimerDeserialization", "Event:OnTimerDeserializationEvent" }, timer.CallLog);
        }

        // Needs two genuinely independent TsTimerTestSubclass instances (owner +
        // remote) rather than one instance with hand-set fields: the point is
        // exercising a REAL _runId produced by a real OnProcessStarted call chain
        // (including a real reentrant restart from inside OnTimerStopped), copied onto
        // a remote as a single simulated packet via TsTimerTestBase.CopySyncedFieldsTo.

        [Test]
        public void OnDeserialization_CoalescedStopThenRestartInOnePacket_FiresStartedForNewRunNotOldRunEnd()
        {
            var owner = CreateProcess<TsTimerTestSubclass>();
            var remote = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(owner);
            SeedAsOwner(remote);

            // Establish the remote's baseline: it has already observed run #1 as running.
            owner.StartTimer(5000);
            int firstRunId = GetRunIdField(owner);
            CopySyncedFieldsTo(owner, remote);
            remote.OnDeserialization();
            Assert.AreEqual(1, remote.OnTimerStartedCount, "Sanity check: baseline observation of run #1.");

            // Coalesce a stop-then-restart into a single subsequent packet: a subscriber
            // reacts to OnTimerStopped by restarting synchronously, so by the time
            // StopTimer() returns, owner's synced state already reflects run #2.
            owner.OnTimerStoppedAction = () => owner.StartTimer(9000);
            owner.StopTimer();
            int secondRunId = GetRunIdField(owner);
            Assert.AreNotEqual(firstRunId, secondRunId, "Sanity check: a real new run really did begin.");

            CopySyncedFieldsTo(owner, remote);
            remote.OnDeserialization();

            Assert.AreEqual(2, remote.OnTimerStartedCount,
                "The coalesced restart must still surface the new run's start to remote clients.");
            Assert.AreEqual(0, remote.OnTimerStoppedCount,
                "The vanished old run's stop was never separately serialized, so it can't be (and must not be guessed at as) reported.");
            Assert.AreEqual(0, remote.OnTimerCompletedCount);
        }

        [Test]
        public void OnDeserialization_CoalescedStopThenRestartWhilePaused_AlsoFiresOnTimerPausedForNewRun()
        {
            var owner = CreateProcess<TsTimerTestSubclass>();
            var remote = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(owner);
            SeedAsOwner(remote);

            owner.StartTimer(5000);
            CopySyncedFieldsTo(owner, remote);
            remote.OnDeserialization();

            owner.OnTimerStoppedAction = () =>
            {
                owner.StartTimer(9000);
                owner.PauseTimer();
            };
            owner.StopTimer();

            CopySyncedFieldsTo(owner, remote);
            remote.OnDeserialization();

            Assert.AreEqual(2, remote.OnTimerStartedCount);
            Assert.AreEqual(1, remote.OnTimerPausedCount,
                "New run is already paused by the time this single coalesced packet arrives, mirroring the late-joiner-while-paused branch.");
        }
    }
}
