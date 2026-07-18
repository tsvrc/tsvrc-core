using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers GetElapsedMilliseconds/GetRemainingMilliseconds/GetElapsedSeconds/
    // GetRemainingSeconds/LastElapsedMilliseconds math, including clamping against
    // server-clock skew and duration overrun. Elapsed time is simulated by shifting
    // _startServerTimeMs backward relative to whatever GetServerTimeMilliseconds()
    // actually returns in this environment (never assumed to be any particular value),
    // rather than waiting on a real clock.
    public class TsTimerElapsedAndRemainingTests : TsTimerTestBase
    {
        [Test]
        public void AllPublicReadAccessors_NeverStarted_ReturnSaneZeroDefaultsWithoutThrowing()
        {
            // Covers the state every TsTimer starts in the instant it's added to a
            // scene, before StartTimer() is ever called - e.g. a UI script reading
            // GetRemainingMilliseconds() to show a countdown before a round begins.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            Assert.DoesNotThrow(() =>
            {
                _ = timer.GetElapsedMilliseconds();
                _ = timer.GetElapsedSeconds();
                _ = timer.GetRemainingMilliseconds();
                _ = timer.GetRemainingSeconds();
                _ = timer.LastElapsedMilliseconds;
                _ = timer.LastElapsedSeconds;
                _ = timer.StartServerTimeMs;
                _ = timer.DurationMs;
                _ = timer.IsProcessRunning();
            });

            Assert.AreEqual(0, timer.GetElapsedMilliseconds());
            Assert.AreEqual(0f, timer.GetElapsedSeconds());
            Assert.AreEqual(0, timer.GetRemainingMilliseconds());
            Assert.AreEqual(0f, timer.GetRemainingSeconds());
            Assert.AreEqual(0, timer.LastElapsedMilliseconds);
            Assert.AreEqual(0f, timer.LastElapsedSeconds);
            Assert.AreEqual(0, timer.StartServerTimeMs);
            Assert.AreEqual(0, timer.DurationMs);
            Assert.IsFalse(timer.IsProcessRunning());
        }

        [Test]
        public void StartServerTimeMs_PublicProperty_ReflectsPrivateAnchorAfterStart()
        {
            // The public StartServerTimeMs property itself (distinct from every other
            // test in this file, which only ever manipulates the private
            // _startServerTimeMs field via reflection to simulate elapsed time) - pins
            // that the exposed accessor actually reads the same anchor StartTimer()
            // wrote, not a stale/default value.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            Assert.AreEqual(0, timer.StartServerTimeMs, "Never started yet, so the anchor is still its default.");

            timer.StartTimer();
            int privateValue = GetStartServerTimeMsField(timer);

            Assert.AreEqual(privateValue, timer.StartServerTimeMs);

            SetStartServerTimeMsField(timer, privateValue - 4321);

            Assert.AreEqual(privateValue - 4321, timer.StartServerTimeMs,
                "The public property must reflect direct writes to the backing field too, not cache a stale copy.");
        }

        [Test]
        public void GetElapsedMilliseconds_Running_ComputesFromAnchors()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            int start = GetStartServerTimeMsField(timer);
            SetStartServerTimeMsField(timer, start - 1234);

            Assert.AreEqual(1234, timer.GetElapsedMilliseconds());
        }

        [Test]
        public void GetElapsedMilliseconds_Stopped_ReturnsFrozenOffsetWithoutTouchingClock()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 500);
            timer.StopTimer();

            // Even if the clock were to advance further, a stopped timer must report the
            // frozen elapsed offset captured at stop time, not a growing value.
            Assert.AreEqual(500, timer.GetElapsedMilliseconds());
        }

        [Test]
        public void GetElapsedMilliseconds_Paused_ReturnsFrozenOffset()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 700);
            timer.PauseTimer();

            Assert.AreEqual(700, timer.GetElapsedMilliseconds());
        }

        [Test]
        public void GetElapsedMilliseconds_NegativeDelta_ClampedToZero()
        {
            // Simulates a server-clock rollback: _startServerTimeMs ends up AHEAD of "now".
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) + 10000);

            Assert.AreEqual(0, timer.GetElapsedMilliseconds());
        }

        [Test]
        public void GetElapsedMilliseconds_OffsetPlusDeltaOverflowsInt_ClampsToZeroNotWrappingNegative()
        {
            // _elapsedOffsetMs + elapsedSinceStartMs is plain (unchecked) int
            // arithmetic; pushed past int.MaxValue it wraps to a large negative
            // number, which the method's own final `elapsedMs < 0 ? 0 : elapsedMs`
            // clamp must catch - proving the "silently resets to 0" contract actually
            // holds at the boundary, rather than wrapping through to some other
            // incorrect (but still positive-looking, so less obviously wrong) value.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetElapsedOffsetMsField(timer, int.MaxValue - 500);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 1000); // delta ~1000ms

            Assert.AreEqual(0, timer.GetElapsedMilliseconds());
        }

        [Test]
        public void StopTimer_ComputeRawElapsedOverflowsInt_FreezesOffsetAtZeroNotWrappingNegative()
        {
            // Same overflow boundary as the test above, but through ComputeRawElapsedMs
            // (OnProcessStopped's own separate implementation of the same delta+clamp
            // pattern, used instead of GetElapsedMilliseconds specifically because
            // _isRunning is already false by the time that hook fires) rather than
            // GetElapsedMilliseconds itself - the two are independent code paths with
            // the same contract, so proving one doesn't prove the other.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetElapsedOffsetMsField(timer, int.MaxValue - 500);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 1000);

            timer.StopTimer();

            Assert.AreEqual(0, GetElapsedOffsetMsField(timer));
        }

        [Test]
        public void GetRemainingMilliseconds_NoDuration_AlwaysZero()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(); // open-ended

            Assert.AreEqual(0, timer.GetRemainingMilliseconds());
        }

        [Test]
        public void GetRemainingMilliseconds_BeforeDuration_ComputesDifference()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 300);

            Assert.AreEqual(700, timer.GetRemainingMilliseconds());
        }

        [Test]
        public void GetRemainingMilliseconds_MatchesTheClassDocCommentsOwnFormula()
        {
            // Asserts the documented formula itself ("remainingMs = DurationMs -
            // GetElapsedMilliseconds()") using the public DurationMs property, across
            // several different elapsed points in the same run.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(2500);
            int start = GetStartServerTimeMsField(timer);

            foreach (int elapsed in new[] { 0, 500, 1300, 2500, 3000 })
            {
                SetStartServerTimeMsField(timer, start - elapsed);
                int expected = timer.DurationMs - timer.GetElapsedMilliseconds();
                if (expected < 0) expected = 0;

                Assert.AreEqual(expected, timer.GetRemainingMilliseconds(),
                    "Mismatch at simulated elapsed=" + elapsed);
            }
        }

        [Test]
        public void GetRemainingMilliseconds_WhilePaused_ComputesFromFrozenElapsedNotRealClock()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 300);
            timer.PauseTimer();

            // Shifting the anchor further back must have zero effect while paused -
            // GetRemainingMilliseconds() must still read the frozen offset, not the
            // (otherwise growing) real elapsed.
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 100000);

            Assert.AreEqual(700, timer.GetRemainingMilliseconds());
        }

        [Test]
        public void GetRemainingMilliseconds_PastDuration_ClampedToZeroNotNegative()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 5000);

            Assert.AreEqual(0, timer.GetRemainingMilliseconds());
        }

        [Test]
        public void SecondsAccessors_AreExactMillisecondConversion()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(2000);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 500);

            Assert.AreEqual(timer.GetElapsedMilliseconds() * 0.001f, timer.GetElapsedSeconds(), 0.0001f);
            Assert.AreEqual(timer.GetRemainingMilliseconds() * 0.001f, timer.GetRemainingSeconds(), 0.0001f);
            Assert.AreEqual(timer.LastElapsedMilliseconds * 0.001f, timer.LastElapsedSeconds, 0.0001f);
        }

        [Test]
        public void LastElapsedMilliseconds_ResetToZeroOnFreshStartAfterPriorRun()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 9000);
            timer.StopTimer();
            Assert.AreEqual(9000, timer.LastElapsedMilliseconds);

            // StopTimer's cleanup clears the synced owner fields (TsProcess.InternalCleanup),
            // same as every other process in this library - a genuinely fresh StartTimer call
            // has to reclaim ownership via Networking.LocalPlayer, which is null outside a real
            // session, so re-seed exactly like starting a brand new instance would need to.
            SeedAsOwner(timer);
            timer.StartTimer();

            Assert.AreEqual(0, timer.LastElapsedMilliseconds);
        }
    }
}
