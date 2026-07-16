using Tsvrc.Timing;
using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    // Covers UpdateElapsedSnapshot's own change-detection logic in isolation - the
    // private helper behind OnTimerUpdatedEvent, called from seven different places
    // (OnDeserialization, OnProcessStarted/Stopped/Completed, ExecutePause/Resume,
    // _TickLocalElapsed). Every scenario here drives the private method directly via
    // reflection with hand-seeded observation state, isolating each of its four
    // independent trigger conditions (elapsed changed / never observed before /
    // running flag changed / paused flag changed) from the other three.
    public class TsTimerUpdateSnapshotTests : TsTimerTestBase
    {
        private static void SeedObserved(TsTimerTestSubclass timer, bool hasObserved, int lastElapsedMs, bool lastIsRunning, bool lastIsPaused)
        {
            PrivateFieldAccess.SetField(timer, "_hasObservedState", hasObserved);
            PrivateFieldAccess.SetField(timer, "_lastObservedElapsedMs", lastElapsedMs);
            PrivateFieldAccess.SetField(timer, "_lastObservedIsRunning", lastIsRunning);
            PrivateFieldAccess.SetField(timer, "_lastObservedIsPaused", lastIsPaused);
        }

        private static void InvokeUpdateElapsedSnapshot(TsTimerTestSubclass timer)
        {
            PrivateFieldAccess.InvokeInstance(timer, "UpdateElapsedSnapshot");
        }

        [Test]
        public void FirstCallEver_HasObservedStateFalse_FiresEventRegardlessOfElapsedValue()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.TsSubscribe(timer, TsTimer.OnTimerUpdatedEvent, nameof(timer._OnTimerUpdatedEventReceived));
            // Fresh instance: _hasObservedState/_isRunning/_isPaused/_elapsedOffsetMs all
            // still at their defaults (false/false/false/0), so a naive "did anything
            // change" comparison against the (equally default) _lastObserved* fields
            // would see no difference at all if not for the explicit !_hasObservedState
            // check.

            InvokeUpdateElapsedSnapshot(timer);

            Assert.AreEqual(1, timer.OnTimerUpdatedCount);
            Assert.IsTrue(GetHasObservedStateField(timer), "Must be marked observed after the first call.");
        }

        [Test]
        public void SteadyState_NothingChanged_DoesNotFireEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            SeedObserved(timer, hasObserved: true, lastElapsedMs: 0, lastIsRunning: false, lastIsPaused: false);
            timer.TsSubscribe(timer, TsTimer.OnTimerUpdatedEvent, nameof(timer._OnTimerUpdatedEventReceived));
            // Not running, not paused, _elapsedOffsetMs still 0 (default) -> GetElapsedMilliseconds()
            // returns 0, exactly matching the seeded observation - nothing has changed.

            InvokeUpdateElapsedSnapshot(timer);

            Assert.AreEqual(0, timer.OnTimerUpdatedCount);
        }

        [Test]
        public void ElapsedValueChanged_RunningAndPausedFlagsUnchanged_FiresEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 250);
            timer.CallLog.Clear();
            timer.TsSubscribe(timer, TsTimer.OnTimerUpdatedEvent, nameof(timer._OnTimerUpdatedEventReceived));
            // isRunning/isPaused stay exactly as OnProcessStarted's own snapshot already
            // observed them (true/false) - only the elapsed value itself moved.

            InvokeUpdateElapsedSnapshot(timer);

            Assert.AreEqual(1, timer.OnTimerUpdatedCount);
            Assert.AreEqual(250, timer.LastElapsedMilliseconds);
        }

        [Test]
        public void RunningFlagChanged_ElapsedValueIdentical_StillFiresEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            // _isRunning stays at its default false, _elapsedOffsetMs stays at its
            // default 0, so GetElapsedMilliseconds() == 0 both before and after this
            // call - the ONLY thing that differs from the seeded observation is the
            // running flag, isolating that one branch of the four-way stateChanged check.
            SeedObserved(timer, hasObserved: true, lastElapsedMs: 0, lastIsRunning: true, lastIsPaused: false);
            timer.TsSubscribe(timer, TsTimer.OnTimerUpdatedEvent, nameof(timer._OnTimerUpdatedEventReceived));

            InvokeUpdateElapsedSnapshot(timer);

            Assert.AreEqual(1, timer.OnTimerUpdatedCount);
        }

        [Test]
        public void PausedFlagChanged_ElapsedValueIdentical_StillFiresEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            SetIsPausedField(timer, true);
            SetElapsedOffsetMsField(timer, 500);
            // While paused, GetElapsedMilliseconds() returns the frozen _elapsedOffsetMs
            // (500) regardless of _startServerTimeMs - matches the seeded observation
            // exactly, so only the paused flag itself differs.
            SeedObserved(timer, hasObserved: true, lastElapsedMs: 500, lastIsRunning: true, lastIsPaused: false);
            timer.TsSubscribe(timer, TsTimer.OnTimerUpdatedEvent, nameof(timer._OnTimerUpdatedEventReceived));

            InvokeUpdateElapsedSnapshot(timer);

            Assert.AreEqual(1, timer.OnTimerUpdatedCount);
        }

        [Test]
        public void AlwaysUpdatesLastElapsedMillisecondsRegardlessOfWhetherEventFires()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            SetIsPausedField(timer, true);
            SetElapsedOffsetMsField(timer, 777);
            SeedObserved(timer, hasObserved: true, lastElapsedMs: 777, lastIsRunning: true, lastIsPaused: true);
            // Genuinely nothing changed this time - included as the companion to the
            // event-firing assertions above, confirming LastElapsedMilliseconds is
            // still refreshed even along the "no event" path, not only when something
            // changed.

            InvokeUpdateElapsedSnapshot(timer);

            Assert.AreEqual(0, timer.OnTimerUpdatedCount);
            Assert.AreEqual(777, timer.LastElapsedMilliseconds);
        }
    }
}
