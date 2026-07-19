using NUnit.Framework;
using Tsvrc.Tracking;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // Covers StartReadyCheck/StopReadyCheck/CompleteReadyCheck, the OnProcessStarted/
    // OnProcessCleanup overrides (including their reentrancy guarantees), and the firing
    // order between ReadyCheckProcess's own OnReadyCheck*Event constants and the inherited
    // PlayerTracker OnTracking*Event constants for the same transition.
    public class ReadyCheckProcessLifecycleTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void StartReadyCheck_Fresh_StartsTrackingAndActivatesReadyCheck()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartReadyCheck(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker));
            Assert.IsTrue(GetReadyCheckActive(tracker));
            Assert.AreEqual(1, tracker.OnReadyCheckStartedCount);
        }

        [Test]
        public void StartReadyCheck_AlwaysUsesProcessUpdateTrue()
        {
            // ReadyCheckProcess.StartReadyCheck hardcodes useProcessUpdate: true - unlike
            // PlayerTracker.StartPlayerTracking, it exposes no parameter to override this,
            // since the 0.5s poll tick is how OnProcessUpdate re-checks readiness.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartReadyCheck(new[] { "A" });

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(tracker, "_useProcessUpdate"));
        }

        [Test]
        public void StartReadyCheck_NullPlayerIds_TreatedAsEmptyAndStillActivates()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);

            Assert.DoesNotThrow(() => tracker.StartReadyCheck(null));

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            Assert.IsTrue(GetReadyCheckActive(tracker));
        }

        [Test]
        public void StopReadyCheck_Running_DeactivatesAndClearsReadyState()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A");

            tracker.StopReadyCheck();

            Assert.IsFalse(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker));
            Assert.IsFalse(GetReadyCheckActive(tracker));
            Assert.AreEqual(1, tracker.OnReadyCheckStoppedCount);
        }

        [Test]
        public void StartReadyCheck_AlreadyRunning_WarnsAndDoesNotRestartOrTouchReadyState()
        {
            // StartReadyCheck has no guard of its own; it forwards straight to
            // PlayerTracker.StartPlayerTracking, whose own already-running check
            // (TsProcess.StartProcess's) must reject a second call untouched.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A");
            Assert.AreEqual(1, tracker.OnReadyCheckStartedCount);

            LogAssert.Expect(LogType.Warning, "[TsVRC] [ReadyCheckProcessTestSubclass] Process is already running.");
            tracker.StartReadyCheck(new[] { "Z" });

            Assert.AreEqual(1, tracker.OnReadyCheckStartedCount,
                "A second call while running must not restart the check.");
            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker),
                "The stray call's payload must never reach _trackedPlayerIds.");
            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(tracker),
                "Existing ready state must survive an already-running call untouched.");
        }

        [Test]
        public void StopReadyCheck_NotRunning_WarnsAndNoOps()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            // Never started.

            LogAssert.Expect(LogType.Warning, "[TsVRC] [ReadyCheckProcessTestSubclass] Process is not running.");
            tracker.StopReadyCheck();

            Assert.AreEqual(0, tracker.OnReadyCheckStoppedCount);
            Assert.IsFalse(GetReadyCheckActive(tracker));
        }

        [Test]
        public void CompleteReadyCheck_NotRunning_WarnsAndNoOps()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);

            LogAssert.Expect(LogType.Warning, "[TsVRC] [ReadyCheckProcessTestSubclass] Process is not running.");
            tracker.CompleteReadyCheck();

            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void CompleteReadyCheck_Running_DeactivatesAndClearsReadyState()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });

            tracker.CompleteReadyCheck();

            Assert.IsFalse(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker));
            Assert.IsFalse(GetReadyCheckActive(tracker));
            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void OnProcessStarted_ReentrantRestartFromOnReadyCheckCompleted_NewRunStartsWithEmptyReadyPlayerIds()
        {
            // Pins the exact scenario ReadyCheckProcess.OnProcessStarted's own comment
            // describes: a subscriber reacting to completion by restarting synchronously
            // must never see the old run's ready ids leak into the new run's own
            // OnReadyCheckStarted hook.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });

            tracker.OnReadyCheckCompletedAction = () => tracker.StartReadyCheck(new[] { "Z" });

            string[] observedDuringLastStartedHook = null;
            tracker.OnReadyCheckStartedAction = () => observedDuringLastStartedHook = GetReadyPlayerIds(tracker);

            tracker.BroadcastAddReadyPlayer("A");
            tracker.BroadcastAddReadyPlayer("B"); // both ready -> completes -> reentrant restart fires

            CollectionAssert.AreEqual(new[] { "Z" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker));
            Assert.IsTrue(GetReadyCheckActive(tracker));
            Assert.AreEqual(2, tracker.OnReadyCheckStartedCount,
                "Once for the original start, once for the reentrant restart.");
            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
            CollectionAssert.AreEqual(new string[0], observedDuringLastStartedHook,
                "OnProcessStarted must clear _readyPlayerIds before base.OnProcessStarted() broadcasts, " +
                "so the reentrant run's own OnReadyCheckStarted never observes the old run's ready ids.");
        }

        [Test]
        public void OnProcessCleanup_ReentrantRestartFromOnProcessStopped_SkipsClearingReadyStateForNewRun()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A");
            tracker.OnProcessStoppedAction = () => tracker.StartReadyCheck(new[] { "Z" });

            tracker.StopReadyCheck();

            Assert.IsTrue(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "Z" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker),
                "Emptied by the reentrant run's own OnProcessStarted clear, not by the old run's cleanup.");
            Assert.IsTrue(GetReadyCheckActive(tracker));
        }

        [Test]
        public void OnProcessCleanup_ReentrantRestartFromOnProcessCompleted_SkipsClearingReadyStateForNewRun()
        {
            // Mirrors the OnProcessStopped reentrancy test above, via CompleteReadyCheck/
            // OnProcessCompletedAction instead - the completion counterpart of the same window.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });
            tracker.OnProcessCompletedAction = () => tracker.StartReadyCheck(new[] { "Z" });

            tracker.CompleteReadyCheck();

            Assert.IsTrue(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "Z" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker),
                "Emptied by the reentrant run's own OnProcessStarted clear, not by the old run's cleanup.");
            Assert.IsTrue(GetReadyCheckActive(tracker));
        }

        [Test]
        public void OnReadyCheckStopped_ReentrantRestartFromWithinHook_NewRunStartsCleanly()
        {
            // Restart triggered from the earliest possible point (OnReadyCheckStopped itself,
            // before even OnTrackingStoppedEvent has fired) rather than from the later
            // OnProcessStopped point the test above uses - a strictly earlier reentrancy window.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A"); // A ready, B not - still running

            tracker.OnReadyCheckStoppedAction = () => tracker.StartReadyCheck(new[] { "Z" });

            tracker.StopReadyCheck();

            Assert.IsTrue(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "Z" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker));
            Assert.AreEqual(1, tracker.OnReadyCheckStoppedCount);
            Assert.AreEqual(2, tracker.OnReadyCheckStartedCount,
                "Once for the original start, once for the reentrant restart.");
        }

        [Test]
        public void EventOrdering_StartTransition_ReadyCheckEventFiresBeforeTrackingEvent()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.TsSubscribe(tracker, ReadyCheckProcess.OnReadyCheckStartedEvent, nameof(tracker._OnReadyCheckStartedEventReceived));
            tracker.TsSubscribe(tracker, PlayerTracker.OnTrackingStartedEvent, nameof(tracker._OnTrackingStartedEventReceived));

            tracker.StartReadyCheck(new[] { "A" });

            CollectionAssert.AreEqual(new[]
            {
                "Hook:OnReadyCheckStarted",
                "Event:OnReadyCheckStartedEvent",
                "Event:OnTrackingStartedEvent",
            }, tracker.CallLog);
        }

        [Test]
        public void EventOrdering_StoppedTransition_ReadyCheckEventFiresBeforeTrackingEvent()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });
            tracker.CallLog.Clear();
            tracker.TsSubscribe(tracker, ReadyCheckProcess.OnReadyCheckStoppedEvent, nameof(tracker._OnReadyCheckStoppedEventReceived));
            tracker.TsSubscribe(tracker, PlayerTracker.OnTrackingStoppedEvent, nameof(tracker._OnTrackingStoppedEventReceived));

            tracker.StopReadyCheck();

            CollectionAssert.AreEqual(new[]
            {
                "Hook:OnReadyCheckStopped",
                "Event:OnReadyCheckStoppedEvent",
                "Event:OnTrackingStoppedEvent",
            }, tracker.CallLog);
        }

        [Test]
        public void EventOrdering_CompletedTransition_ReadyCheckEventFiresBeforeTrackingEvent()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });
            tracker.CallLog.Clear();
            tracker.TsSubscribe(tracker, ReadyCheckProcess.OnReadyCheckCompletedEvent, nameof(tracker._OnReadyCheckCompletedEventReceived));
            tracker.TsSubscribe(tracker, PlayerTracker.OnTrackingCompletedEvent, nameof(tracker._OnTrackingCompletedEventReceived));

            tracker.CompleteReadyCheck();

            CollectionAssert.AreEqual(new[]
            {
                "Hook:OnReadyCheckCompleted",
                "Event:OnReadyCheckCompletedEvent",
                "Event:OnTrackingCompletedEvent",
            }, tracker.CallLog);
        }

        // LastPlayerIds (inherited from PlayerTracker) is assigned before OnTrackingStarted -
        // and therefore before ReadyCheckProcess's own OnReadyCheckStarted, which fires nested
        // inside it - runs. Every OnReadyCheck*Event's own doc comment says "Read LastPlayerIds
        // in your callback"; the following three tests pin that the data is actually already
        // correct at that point, not just that the hooks fire in the right relative order
        // (already covered by the EventOrdering tests above). Mirrors PlayerTrackerLifecycleTests'
        // own LastPlayerIdsInsideOnTracking*-style pattern, one level further in for this class's
        // own, earlier-firing hooks.

        [Test]
        public void OnReadyCheckStarted_LastPlayerIdsAlreadyCurrentInsideHook()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartReadyCheck(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIdsInsideOnReadyCheckStarted);
        }

        [Test]
        public void OnReadyCheckStopped_LastPlayerIdsStillPreClearValueInsideHook()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });

            tracker.StopReadyCheck();

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIdsInsideOnReadyCheckStopped,
                "OnReadyCheckStopped must observe the pre-cleanup tracked set, not the post-cleanup empty one.");
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds,
                "By the time StopReadyCheck returns, cleanup has already run and cleared it.");
        }

        [Test]
        public void OnReadyCheckCompleted_LastPlayerIdsStillPreClearValueInsideHook()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });

            tracker.CompleteReadyCheck();

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIdsInsideOnReadyCheckCompleted);
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }

        [Test]
        public void FullLifecycle_StartMultipleReadyStop_DoesNotThrowAndEndsClean()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);

            Assert.DoesNotThrow(() =>
            {
                tracker.StartReadyCheck(new[] { "A", "B", "C" });
                tracker.BroadcastAddReadyPlayer("A");
                tracker.BroadcastRemoveReadyPlayer("A");
                tracker.BroadcastAddReadyPlayer("B");
                tracker.StopReadyCheck();
            });

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetReadyPlayerIds(tracker));
        }
    }
}
