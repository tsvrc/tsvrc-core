using NUnit.Framework;
using Tsvrc.Timing;

namespace Tsvrc.Tests.EditMode
{
    // Covers PauseTimer/ResumeTimer and their RequestPauseTimer/RequestResumeTimer
    // network-callable counterparts, on the owner-direct path (SeedAsOwner makes
    // IsProcessOwner() true, short-circuiting the non-owner SendCustomNetworkEvent
    // forward before Networking.IsOwner is ever evaluated - same rationale as every
    // other class in this suite, see TsProcessTestBase's header comment), plus the
    // dual-authority fallback path (IsProcessOwner() false but Networking.IsOwner(
    // gameObject) true) near the bottom of this file. The pure non-owner-forwarding
    // branch (both false) needs a real second networked client and cannot be
    // constructed in Edit Mode or ClientSim, since Networking.IsOwner(gameObject) is
    // true for a bare, unowned GameObject in both environments.
    public class TsTimerPauseResumeTests : TsTimerTestBase
    {
        [Test]
        public void PauseTimer_Running_FreezesElapsedAndFiresOnTimerPaused()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 300);

            timer.PauseTimer();

            Assert.IsTrue(GetIsPausedField(timer));
            Assert.AreEqual(300, GetElapsedOffsetMsField(timer));
            Assert.AreEqual(1, timer.OnTimerPausedCount);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(timer, "_localUpdateLoopActive"));
        }

        [Test]
        public void PauseTimer_NotRunning_NoOp()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            timer.PauseTimer();

            Assert.AreEqual(0, timer.OnTimerPausedCount);
        }

        [Test]
        public void PauseTimer_AlreadyPaused_NoOpNoDuplicateEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.PauseTimer();

            timer.PauseTimer();

            Assert.AreEqual(1, timer.OnTimerPausedCount);
        }

        [Test]
        public void ResumeTimer_Paused_MovesAnchorForwardAndFiresOnTimerResumed()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 300);
            timer.PauseTimer();

            timer.ResumeTimer();

            Assert.IsFalse(GetIsPausedField(timer));
            Assert.AreEqual(1, timer.OnTimerResumedCount);
            // Elapsed must still read as (at least) the frozen 300ms immediately after resume,
            // since the anchor was moved to "now" and the offset preserved.
            Assert.GreaterOrEqual(timer.GetElapsedMilliseconds(), 300);
        }

        [Test]
        public void ResumeTimer_NotRunning_NoOp()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            timer.ResumeTimer();

            Assert.AreEqual(0, timer.OnTimerResumedCount);
        }

        [Test]
        public void ResumeTimer_NotPaused_NoOp()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();

            timer.ResumeTimer();

            Assert.AreEqual(0, timer.OnTimerResumedCount);
        }

        [Test]
        public void PauseThenStop_ElapsedOffsetNotDoubleCounted()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 400);
            timer.PauseTimer();

            // If OnProcessStopped incorrectly re-computed elapsed from the (unmoved,
            // still-stale) start anchor instead of skipping that step while paused, this
            // would inflate past 400. It must not.
            timer.StopTimer();

            Assert.AreEqual(400, timer.GetElapsedMilliseconds());
        }

        [Test]
        public void PauseThenComplete_ElapsedOffsetNotDoubleCounted()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer(1000);
            SetStartServerTimeMsField(timer, GetStartServerTimeMsField(timer) - 400);
            timer.PauseTimer();

            timer.CompleteProcess();

            Assert.AreEqual(400, timer.GetElapsedMilliseconds());
            Assert.IsTrue(GetWasCompletedField(timer));
        }

        [Test]
        public void RequestPauseTimer_OwnerDirectCall_PausesJustLikePauseTimer()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();

            timer.RequestPauseTimer();

            Assert.IsTrue(GetIsPausedField(timer));
            Assert.AreEqual(1, timer.OnTimerPausedCount);
        }

        [Test]
        public void RequestPauseTimer_NotRunning_NoOp()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            timer.RequestPauseTimer();

            Assert.AreEqual(0, timer.OnTimerPausedCount);
        }

        [Test]
        public void RequestResumeTimer_OwnerDirectCall_ResumesJustLikeResumeTimer()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.PauseTimer();

            timer.RequestResumeTimer();

            Assert.IsFalse(GetIsPausedField(timer));
            Assert.AreEqual(1, timer.OnTimerResumedCount);
        }

        [Test]
        public void RequestResumeTimer_NotPaused_NoOp()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();

            timer.RequestResumeTimer();

            Assert.AreEqual(0, timer.OnTimerResumedCount);
        }

        [Test]
        public void RequestPauseTimer_AlreadyPaused_NoOp()
        {
            // RequestPauseTimer's own guard is `!IsProcessRunning() || _isPaused` - the
            // "not running" half is covered by RequestPauseTimer_NotRunning_NoOp above;
            // this covers the other half directly on the [NetworkCallable] entry point
            // itself rather than only through the already-paused coverage on the public
            // PauseTimer() wrapper.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.RequestPauseTimer();

            timer.RequestPauseTimer();

            Assert.AreEqual(1, timer.OnTimerPausedCount);
        }

        [Test]
        public void RequestResumeTimer_NotRunning_NoOp()
        {
            // Mirrors RequestPauseTimer_AlreadyPaused_NoOp above - the other half of
            // RequestResumeTimer's own `!IsProcessRunning() || !_isPaused` guard,
            // checked directly on the [NetworkCallable] entry point.
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);

            timer.RequestResumeTimer();

            Assert.AreEqual(0, timer.OnTimerResumedCount);
        }

        [Test]
        public void EventOrdering_Pause_HookFiresBeforeEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.CallLog.Clear();
            timer.TsSubscribe(timer, TsTimer.OnTimerPausedEvent, nameof(timer._OnTimerPausedEventReceived));

            timer.PauseTimer();

            CollectionAssert.AreEqual(new[] { "Hook:OnTimerPaused", "Event:OnTimerPausedEvent" }, timer.CallLog);
        }

        [Test]
        public void EventOrdering_Resume_HookFiresBeforeEvent()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.PauseTimer();
            timer.CallLog.Clear();
            timer.TsSubscribe(timer, TsTimer.OnTimerResumedEvent, nameof(timer._OnTimerResumedEventReceived));

            timer.ResumeTimer();

            CollectionAssert.AreEqual(new[] { "Hook:OnTimerResumed", "Event:OnTimerResumedEvent" }, timer.CallLog);
        }

        // Networking.IsOwner(gameObject) returns true for a bare, never-networked
        // GameObject in Edit Mode, with no networking system running at all - every
        // test above uses SeedAsOwner, which makes IsProcessOwner() true and
        // short-circuits `!IsProcessOwner() && !Networking.IsOwner(gameObject)` to
        // false before Networking.IsOwner is ever evaluated, so these two tests seed
        // IsProcessOwner() false instead to reach the other branch. Since
        // Networking.IsOwner(gameObject) is true here, the dual-authority fallback
        // (`... || Networking.IsOwner(gameObject)`, the same "we accept authority if
        // we are either the process owner by ID or the Unity owner" fallback
        // documented on TsProcess.StopProcess) executes locally even when
        // _ownerPlayerIdInt names someone else.
        [Test]
        public void PauseTimer_OwnerIdMismatchButLocalHoldsUnityOwnership_ExecutesViaDualAuthorityFallback()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            PrivateFieldAccess.SetField(timer, "_localPlayerIdInt", 1);
            PrivateFieldAccess.SetField(timer, "_localPlayerId", "Local#1");
            PrivateFieldAccess.SetField(timer, "_ownerPlayerIdInt", 2);
            PrivateFieldAccess.SetField(timer, "_ownerId", "Remote#2");

            Assert.DoesNotThrow(() => timer.PauseTimer(),
                "Networking.IsOwner(gameObject) on a bare, never-networked GameObject must not throw outside Play Mode.");

            Assert.IsTrue(GetIsPausedField(timer),
                "Local holds real Unity ownership (confirmed: Networking.IsOwner(gameObject) is true here), " +
                "so the dual-authority fallback executes the pause directly despite the _ownerPlayerIdInt mismatch.");
            Assert.AreEqual(1, timer.OnTimerPausedCount);
        }

        [Test]
        public void ResumeTimer_OwnerIdMismatchButLocalHoldsUnityOwnership_ExecutesViaDualAuthorityFallback()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            SetIsPausedField(timer, true);
            PrivateFieldAccess.SetField(timer, "_localPlayerIdInt", 1);
            PrivateFieldAccess.SetField(timer, "_localPlayerId", "Local#1");
            PrivateFieldAccess.SetField(timer, "_ownerPlayerIdInt", 2);
            PrivateFieldAccess.SetField(timer, "_ownerId", "Remote#2");

            Assert.DoesNotThrow(() => timer.ResumeTimer());

            Assert.IsFalse(GetIsPausedField(timer),
                "Same dual-authority fallback as the pause test above - executes locally, doesn't forward.");
            Assert.AreEqual(1, timer.OnTimerResumedCount);
        }

        [Test]
        public void ReentrantResumeFromOnTimerPaused_TakesEffectImmediately()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.OnTimerPausedAction = () => timer.ResumeTimer();

            Assert.DoesNotThrow(() => timer.PauseTimer());

            Assert.IsFalse(GetIsPausedField(timer), "The reentrant ResumeTimer() call must have taken effect.");
            Assert.AreEqual(1, timer.OnTimerPausedCount);
            Assert.AreEqual(1, timer.OnTimerResumedCount);
        }

        [Test]
        public void ReentrantPauseFromOnTimerResumed_TakesEffectImmediately()
        {
            var timer = CreateProcess<TsTimerTestSubclass>();
            SeedAsOwner(timer);
            timer.StartTimer();
            timer.PauseTimer();
            timer.OnTimerResumedAction = () => timer.PauseTimer();

            Assert.DoesNotThrow(() => timer.ResumeTimer());

            Assert.IsTrue(GetIsPausedField(timer), "The reentrant PauseTimer() call must have taken effect.");
            Assert.AreEqual(2, timer.OnTimerPausedCount, "Once for the original pause, once for the reentrant re-pause.");
            Assert.AreEqual(1, timer.OnTimerResumedCount);
        }
    }
}
