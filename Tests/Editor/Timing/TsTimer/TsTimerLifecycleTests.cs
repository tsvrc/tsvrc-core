using NUnit.Framework;
using Tsvrc.Timing;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // Covers StartTimer()/StartTimer(ms)/StopTimer, the OnProcessUpdate auto-complete
    // boundary, OnProcessCleanup's field resets, reentrant restart from inside
    // OnTimerStopped/OnTimerCompleted, and the local (every-client) elapsed tick loop.
    public class TsTimerLifecycleTests : TsTimerTestBase
    {
        [Test]
        public void StartTimer_Parameterless_OpenEndedNoProcessUpdate()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            int runIdBefore = GetRunIdField(timer);

            timer.StartTimer();

            Assert.AreEqual(0, timer.DurationMs);
            Assert.IsTrue(timer.IsProcessRunning());
            Assert.AreEqual(1, timer.OnTimerStartedCount);
            Assert.AreEqual(runIdBefore + 1, GetRunIdField(timer));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(timer, "_useProcessUpdate"),
                "Open-ended timer has nothing to auto-complete against, so no tick loop is needed.");
        }

        [Test]
        public void StartTimer_Parameterless_AlreadyRunning_SilentlyNoOps()
        {
            // Unlike TsProcess.StartProcess's own "already running" guard (which logs
            // a warning), TsTimer.StartTimer()'s own outer IsProcessRunning() guard
            // returns before ever reaching StartProcess - so a redundant call while
            // running is a silent no-op here, not a warning.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();

            timer.StartTimer();

            Assert.AreEqual(1, timer.OnTimerStartedCount);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void StartTimer_PositiveDuration_SetsDurationAndEnablesProcessUpdate()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            timer.StartTimer(5000);

            Assert.AreEqual(5000, timer.DurationMs);
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(timer, "_useProcessUpdate"));
        }

        [Test]
        public void StartTimer_ZeroDuration_TreatedAsOpenEnded()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            timer.StartTimer(0);

            Assert.AreEqual(0, timer.DurationMs);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(timer, "_useProcessUpdate"));
        }

        [Test]
        public void StartTimer_NegativeDuration_CoercedToZero()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            timer.StartTimer(-500);

            Assert.AreEqual(0, timer.DurationMs);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(timer, "_useProcessUpdate"));
        }

        [Test]
        public void StartTimer_WithDuration_AlreadyRunning_SilentlyLeavesExistingDurationUntouched()
        {
            // Same silent-no-op guard as the parameterless overload above - no warning.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);

            timer.StartTimer(9999);

            Assert.AreEqual(1000, timer.DurationMs,
                "StartTimer(ms) guards against mutating _durationMs while already running.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void StopTimer_Running_FiresOnTimerStoppedAndClearsRunningState()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(5000);

            timer.StopTimer();

            Assert.IsFalse(timer.IsProcessRunning());
            Assert.AreEqual(1, timer.OnTimerStoppedCount);
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
            Assert.IsFalse(GetWasCompletedField(timer));
            Assert.AreEqual(0, GetDurationMsField(timer));
            Assert.IsFalse(GetIsPausedField(timer));
        }

        [Test]
        public void StopTimer_NotRunning_WarnsAndNoOps()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            LogAssert.Expect(LogType.Warning, "[TsProcess] Process is not running.");
            timer.StopTimer();

            Assert.AreEqual(0, timer.OnTimerStoppedCount);
        }

        [Test]
        public void OnProcessUpdate_ElapsedBelowDuration_DoesNotComplete()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            int start = GetStartServerTimeMsField(timer);
            SetStartServerTimeMsField(timer, start - 999);

            ForceNextTickDueNow(timer);
            timer._TickProcessUpdate();

            Assert.IsTrue(timer.IsProcessRunning());
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
        }

        [Test]
        public void OnProcessUpdate_ElapsedExactlyAtDuration_Completes()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            int start = GetStartServerTimeMsField(timer);
            SetStartServerTimeMsField(timer, start - 1000);

            ForceNextTickDueNow(timer);
            timer._TickProcessUpdate();

            Assert.IsFalse(timer.IsProcessRunning());
            Assert.AreEqual(1, timer.OnTimerCompletedCount);
            Assert.IsTrue(GetWasCompletedField(timer));
        }

        [Test]
        public void OnProcessUpdate_OpenEnded_NeverAutoCompletesAcrossManyTicks()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(); // open-ended, so OnProcessUpdate is invoked directly below rather than via a real tick loop
            PrivateFieldAccess.InvokeInstance(timer, "OnProcessUpdate");
            PrivateFieldAccess.InvokeInstance(timer, "OnProcessUpdate");

            Assert.IsTrue(timer.IsProcessRunning());
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
        }

        [Test]
        public void OnProcessUpdate_Paused_ElapsedFrozen_NeverCompletesEvenPastDuration()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            int start = GetStartServerTimeMsField(timer);
            SetStartServerTimeMsField(timer, start - 500);
            timer.PauseTimer(); // freezes _elapsedOffsetMs at ~500

            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 100000); // would blow past duration if not frozen
            PrivateFieldAccess.InvokeInstance(timer, "OnProcessUpdate");

            Assert.IsTrue(timer.IsProcessRunning());
            Assert.AreEqual(0, timer.OnTimerCompletedCount);
        }

        [Test]
        public void PauseTimer_ExtendsEffectiveDuration_CompletesOnlyAfterFullRemainingElapsesPostResume()
        {
            // Pins the class's own doc comment on StartTimer(int): "Pausing extends
            // the effective run time so the full duration is always observed." The
            // test above already proves elapsed is frozen WHILE paused; this proves
            // the end-to-end consequence through the real auto-complete tick path:
            // after resuming, the timer must still require the full REMAINING 600ms
            // (1000 duration - 400 already elapsed at pause time) to elapse before
            // completing, not just "some" additional time, and not complete instantly
            // on resume even though a huge amount of real/simulated time passed while
            // paused.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            int start = GetStartServerTimeMsField(timer);
            SetStartServerTimeMsField(timer, start - 400);
            timer.PauseTimer();

            // Simulate a huge amount of real time passing while paused - must have no
            // effect on the eventually-required remaining duration.
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 999999);
            timer.ResumeTimer(); // moves anchor to "now" (whatever GetServerTimeMilliseconds() is), preserving the 400ms offset

            int postResumeStart = GetStartServerTimeMsField(timer);

            // Not yet at the full remaining 600ms since resume - must not complete.
            SetStartServerTimeMsField(timer, postResumeStart - 599);
            ForceNextTickDueNow(timer);
            timer._TickProcessUpdate();
            Assert.IsTrue(timer.IsProcessRunning(), "Must not complete 1ms before the full remaining duration has elapsed since resume.");
            Assert.AreEqual(0, timer.OnTimerCompletedCount);

            // Exactly the full remaining 600ms since resume - must complete now.
            SetStartServerTimeMsField(timer, postResumeStart - 600);
            ForceNextTickDueNow(timer);
            timer._TickProcessUpdate();
            Assert.IsFalse(timer.IsProcessRunning());
            Assert.AreEqual(1, timer.OnTimerCompletedCount);
        }

        [Test]
        public void LocalUpdateInterval_ConstantIsPinnedAtQuarterSecond()
        {
            // Regression guard for the 0.25s local (every-client) elapsed-refresh cadence.
            float interval = PrivateFieldAccess.GetField<float>(typeof(TsTimer), "_localUpdateInterval");

            Assert.AreEqual(0.25f, interval);
        }

        [Test]
        public void OnProcessCleanup_AfterStop_PreservesFinalAnchorsResetsDurationAndPause()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(5000);
            int start = GetStartServerTimeMsField(timer);
            SetStartServerTimeMsField(timer, start - 200);

            timer.StopTimer();

            Assert.AreEqual(0, GetDurationMsField(timer));
            Assert.IsFalse(GetIsPausedField(timer));
            Assert.AreEqual(start - 200, GetStartServerTimeMsField(timer),
                "Final run anchors are preserved after cleanup so non-owners can display the final elapsed time.");
        }

        [Test]
        public void OnProcessCleanup_AfterComplete_WasCompletedTrue()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 1000);
            ForceNextTickDueNow(timer);

            timer._TickProcessUpdate();

            Assert.IsTrue(GetWasCompletedField(timer));
        }

        [Test]
        public void ReentrantRestartFromOnTimerStopped_NewRunNotClobberedByOldCleanup()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            int firstRunId = GetRunIdField(timer);

            timer.OnTimerStoppedAction = () => timer.StartTimer(2000);

            timer.StopTimer();

            Assert.IsTrue(timer.IsProcessRunning(), "The reentrant restart's own run must still be active.");
            Assert.AreEqual(2000, timer.DurationMs);
            Assert.AreEqual(firstRunId + 1, GetRunIdField(timer));
            Assert.AreEqual(1, timer.OnTimerStoppedCount);
            Assert.AreEqual(2, timer.OnTimerStartedCount, "Once for the original start, once for the reentrant restart.");
        }

        [Test]
        public void ReentrantRestartFromOnTimerCompleted_NewRunNotClobberedByOldCleanup()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 1000);
            ForceNextTickDueNow(timer);

            timer.OnTimerCompletedAction = () => timer.StartTimer(3000);

            timer._TickProcessUpdate();

            Assert.IsTrue(timer.IsProcessRunning());
            Assert.AreEqual(3000, timer.DurationMs);
            Assert.AreEqual(1, timer.OnTimerCompletedCount);
            Assert.AreEqual(2, timer.OnTimerStartedCount);
        }

        [Test]
        public void ReentrantStopFromOnTimerStarted_UnwindsCleanlyAndSkipsTheBaseClassPostStartTickSchedule()
        {
            // This is the earliest possible reentrancy window: OnProcessStarted (base
            // TsProcess.StartProcess's own callback) calls TsTimer.OnTimerStarted()
            // as its literal last statement, which itself runs BEFORE control returns to
            // StartProcess()'s own post-callback `if (_useProcessUpdate && !_updateLoopActive)`
            // tick-scheduling check - so a full stop+cleanup can complete entirely
            // before that check ever runs, on the very run it belongs to.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.OnTimerStartedAction = () => timer.StopTimer();

            Assert.DoesNotThrow(() => timer.StartTimer(1000));

            Assert.IsFalse(timer.IsProcessRunning(), "The reentrant StopTimer() call must have taken effect.");
            Assert.AreEqual(1, timer.OnTimerStartedCount);
            Assert.AreEqual(1, timer.OnTimerStoppedCount);
            Assert.AreEqual(0, timer.DurationMs, "OnProcessCleanup already reset it after the reentrant stop.");
        }

        [Test]
        public void EventOrdering_Start_HookFiresBeforeEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.TsSubscribe(timer, TsTimer.OnTimerStartedEvent, nameof(timer._OnTimerStartedEventReceived));

            timer.StartTimer();

            CollectionAssert.AreEqual(new[] { "Hook:OnTimerStarted", "Event:OnTimerStartedEvent" }, timer.CallLog);
        }

        [Test]
        public void EventOrdering_Stop_HookFiresBeforeEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.CallLog.Clear();
            timer.TsSubscribe(timer, TsTimer.OnTimerStoppedEvent, nameof(timer._OnTimerStoppedEventReceived));

            timer.StopTimer();

            CollectionAssert.AreEqual(new[] { "Hook:OnTimerStopped", "Event:OnTimerStoppedEvent" }, timer.CallLog);
        }

        [Test]
        public void TickLocalElapsed_InactiveLoop_IsNoOp()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            Assert.DoesNotThrow(() => timer._TickLocalElapsed());
        }

        [Test]
        public void TickLocalElapsed_ActiveWhileRunning_KeepsLoopActive()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();

            Assert.IsTrue(GetLocalUpdateLoopActiveField(timer));
            timer._TickLocalElapsed();
            Assert.IsTrue(GetLocalUpdateLoopActiveField(timer));
        }

        [Test]
        public void TickLocalElapsed_AfterStop_DeactivatesLoop()
        {
            // OnProcessStopped already calls StopLocalUpdateLoop() directly, so simulate
            // a stale already-scheduled tick arriving afterward by forcing the flag back
            // on - this is the only way to exercise _TickLocalElapsed's own
            // !IsProcessRunning() self-deactivation branch instead of just observing a
            // flag some other code path already cleared.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.StopTimer();
            PrivateFieldAccess.SetField(timer, "_localUpdateLoopActive", true);

            timer._TickLocalElapsed();

            Assert.IsFalse(GetLocalUpdateLoopActiveField(timer));
        }

        [Test]
        public void FullLifecycle_StartPauseResumeStopRestartComplete_EndToEndDoesNotThrowAndEndsClean()
        {
            // One continuous run through every lifecycle transition the class
            // supports, in sequence - an integration/smoke check that the pieces
            // compose correctly. Each transition's own specific behavior is covered
            // in isolation elsewhere in this file and in TsTimerPauseResumeTests.cs.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            Assert.DoesNotThrow(() =>
            {
                timer.StartTimer(5000);
                timer.PauseTimer();
                timer.ResumeTimer();
                timer.StopTimer();

                // StopTimer's cleanup clears the synced owner fields (same
                // TsProcess.InternalCleanup behavior the elapsed-math tests in
                // TsTimerElapsedAndRemainingTests.cs rely on), so a genuinely fresh
                // restart needs re-seeding.
                SeedAsOwner(timer);
                timer.StartTimer(1000);
                int start = GetStartServerTimeMsField(timer);
                SetStartServerTimeMsField(timer, start - 1000);
                ForceNextTickDueNow(timer);
                timer._TickProcessUpdate(); // auto-completes
            });

            Assert.IsFalse(timer.IsProcessRunning());
            Assert.IsTrue(GetWasCompletedField(timer));
            Assert.AreEqual(0, GetDurationMsField(timer));
            Assert.IsFalse(GetIsPausedField(timer));
            Assert.AreEqual(2, timer.OnTimerStartedCount);
            Assert.AreEqual(1, timer.OnTimerStoppedCount);
            Assert.AreEqual(1, timer.OnTimerCompletedCount);
            Assert.AreEqual(1, timer.OnTimerPausedCount);
            Assert.AreEqual(1, timer.OnTimerResumedCount);
        }
    }
}
