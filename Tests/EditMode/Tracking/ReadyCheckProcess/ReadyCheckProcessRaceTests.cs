using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Dedicated race/timing coverage beyond what the other feature files already pin
    // incidentally. Traces interactions that only show up when a call lands in a narrow
    // window most tests never construct on purpose.
    public class ReadyCheckProcessRaceTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void NotifyTrackedPlayersRemoved_DirectCallDuringOwnerMidCleanupWindow_CannotCauseIncorrectCompletion()
        {
            // Unlike its Broadcast* siblings (BroadcastAddTrackedPlayers/BroadcastRemoveTrackedPlayers/
            // BroadcastAddReadyPlayer/BroadcastRemoveReadyPlayer, all guarded by IsProcessRunning()),
            // PlayerTracker.NotifyTrackedPlayersRemoved has no running check at all - by design,
            // documented in its own class's "Security model": it only authenticates the caller as
            // the real owner, not the local running state, since a legitimate late-arriving
            // notification can validly land after the local mirror has already moved on.
            // OnTrackingPlayersRemoved's own guard is correspondingly just `!IsProcessOwner()`.
            //
            // This means a call reaching it during the mid-cleanup window (_isRunning already
            // false, but IsProcessOwner() still true - the same window PlayerTracker's own
            // Broadcast* methods explicitly guard against reentering) is possible in principle.
            // This test pins that it's still safe: CheckAllPlayersReady()'s resulting
            // CompleteReadyCheck() call is rejected by Process.CompleteProcess()'s own
            // _isRunning guard (a warning log, not an incorrect completion).
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A"); // A ready, B not - still running

            tracker.OnProcessStoppedAction = () =>
            {
                // Simulate "B" having already left by some other means, isolating what's under
                // test to OnTrackingPlayersRemoved's own reaction rather than the removal
                // mechanics already covered elsewhere. _isBroadcasting bypasses the Edit-Mode
                // limitation that NetworkCalling.CallingPlayer always reads null (see
                // PlayerTrackerNotifyTests for the same technique applied to PlayerTracker's
                // own Notify* methods).
                SetTrackedPlayerIds(tracker, new[] { "A" });
                PrivateFieldAccess.SetField(tracker, "_isBroadcasting", true);
                LogAssert.Expect(LogType.Warning, "[TsVRC] [ReadyCheckProcessTestSubclass] Process is not running.");
                Assert.DoesNotThrow(() => tracker.NotifyTrackedPlayersRemoved(new[] { "B" }));
                PrivateFieldAccess.SetField(tracker, "_isBroadcasting", false);
            };

            tracker.StopReadyCheck();

            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount,
                "The mid-cleanup-window call must never be able to complete the (already-stopping) check.");
            Assert.AreEqual(1, tracker.OnReadyCheckStoppedCount);
            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastRemoveReadyPlayer_CalledFromInsideOnReadyCheckCompleted_RejectedByIsProcessRunningGuard()
        {
            // ExecuteComplete sets _isRunning=false BEFORE calling OnProcessCompleted(), which is
            // what eventually reaches OnReadyCheckCompleted (via OnTrackingCompleted). A subscriber
            // reacting to completion by trying to un-ready one of the just-completed players must
            // be rejected by BroadcastRemoveReadyPlayer's own IsProcessRunning() guard - _trackedPlayerIds/
            // _readyPlayerIds are still the pre-cleanup ["A","B"] at this point (OnProcessCleanup
            // hasn't run yet), so only the running guard - not IsTrackedPlayer/IsPlayerReady - is
            // what's actually being isolated and tested here.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A"); // A ready, B not - still running
            tracker.OnReadyCheckCompletedAction = () => tracker.BroadcastRemoveReadyPlayer("A");

            Assert.DoesNotThrow(() => tracker.BroadcastAddReadyPlayer("B")); // both ready -> completes, action fires inline

            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker),
                "Cleared by the normal completion cleanup - the rejected reentrant remove had no effect either way.");
        }

        [Test]
        public void SetReady_CalledFromInsideOnReadyCheckCompleted_RejectedByReadyCheckActiveGateBeforeEvenReachingOwnerBranch()
        {
            // _readyCheckActive is set false by OnTrackingCompleted BEFORE OnReadyCheckCompleted
            // fires - a full guard-generation earlier than IsProcessRunning(). This confirms
            // SetReady's own top-level gate independently protects this window too, not just the
            // owner branch's explicit IsProcessRunning() re-check.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "TestOwner#" + OwnerPlayerId });
            bool readyCheckActiveInsideHook = true;
            tracker.OnReadyCheckCompletedAction = () =>
            {
                readyCheckActiveInsideHook = GetReadyCheckActive(tracker);
                Assert.DoesNotThrow(() => tracker.SetReady(false));
            };

            tracker.SetReady(); // sole tracked player readies up -> auto-completes

            Assert.IsFalse(readyCheckActiveInsideHook,
                "_readyCheckActive must already be false by the time OnReadyCheckCompleted fires.");
            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void BroadcastAddReadyPlayer_CalledFromInsideOnReadyCheckStopped_RejectedByIsProcessRunningGuard()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" }); // neither ready
            tracker.OnReadyCheckStoppedAction = () => tracker.BroadcastAddReadyPlayer("A");

            tracker.StopReadyCheck();

            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount, "Must never complete via a rejected reentrant add.");
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker),
                "Cleared by the normal stop cleanup - the rejected reentrant add had no effect either way.");
        }

        [Test]
        public void SetReady_CalledFromInsideOnReadyCheckStarted_SeesTheJustStartedTrackedSet()
        {
            // Confirms there's no ordering gap between _trackedPlayerIds being written
            // (PlayerTracker.OnProcessStarted, called from inside ReadyCheckProcess.OnProcessStarted's
            // base call) and OnReadyCheckStarted firing (also inside that same base call, one level
            // deeper via OnTrackingStarted) - a SetReady() call made synchronously from inside the
            // hook must already see itself as tracked.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            bool wasTrackedInsideHook = false;
            tracker.OnReadyCheckStartedAction = () =>
            {
                wasTrackedInsideHook = InvokeIsTrackedPlayer(tracker, ownerId);
                tracker.SetReady();
            };

            tracker.StartReadyCheck(new[] { ownerId, "Other#1" });

            Assert.IsTrue(wasTrackedInsideHook);
            Assert.IsTrue(InvokeIsPlayerReady(tracker, ownerId),
                "SetReady() called synchronously from inside OnReadyCheckStarted must succeed, " +
                "not silently no-op due to a staleness gap between tracked-set and hook timing.");
        }

        [Test]
        public void BroadcastAddReadyPlayer_RaceAgainstConcurrentRemoval_SameFrame_NeverLeavesInconsistentState()
        {
            // Simulates the two legitimate orderings of "a player readies up right as they leave":
            // ready-then-removed and removed-then-ready-attempt. Both must converge on the same
            // safe end state - the departed player counted as neither tracked nor ready - since
            // Udon is single-threaded and each call fully completes before the next begins.
            var trackerA = CreateProcess<ReadyCheckProcessTestSubclass>("Order1");
            SeedAsOwner(trackerA);
            trackerA.StartReadyCheck(new[] { "TestOwner#" + OwnerPlayerId, "Leaver#9" });
            trackerA.BroadcastAddReadyPlayer("Leaver#9"); // readies up right before leaving
            trackerA.RemoveTrackedPlayers(new[] { "Leaver#9" }); // then leaves

            Assert.IsFalse(InvokeIsTrackedPlayer(trackerA, "Leaver#9"));
            CollectionAssert.DoesNotContain(GetReadyPlayerIds(trackerA), "Leaver#9");

            var trackerB = CreateProcess<ReadyCheckProcessTestSubclass>("Order2");
            SeedAsOwner(trackerB);
            trackerB.StartReadyCheck(new[] { "TestOwner#" + OwnerPlayerId, "Leaver#9" });
            trackerB.RemoveTrackedPlayers(new[] { "Leaver#9" }); // leaves first
            trackerB.BroadcastAddReadyPlayer("Leaver#9"); // a stale ready request arrives after

            Assert.IsFalse(InvokeIsTrackedPlayer(trackerB, "Leaver#9"));
            CollectionAssert.DoesNotContain(GetReadyPlayerIds(trackerB), "Leaver#9",
                "A stale ready request for an already-untracked player must be rejected by the IsTrackedPlayer guard.");
        }
    }
}
