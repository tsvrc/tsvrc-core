using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // Covers StartPlayerTracking/StopPlayerTracking/CompletePlayerTracking and the
    // OnProcessStarted/Stopped/Completed/Cleanup overrides. SendCustomNetworkEvent's
    // Editor-proxy implementation reflection-invokes the named method on the same instance
    // synchronously regardless of NetworkEventTarget, so the full owner round trip
    // (broadcast -> Notify* -> LastPlayerIds/hook/TsEmit) is exercised for free by just
    // calling the public API - no need to invoke Notify* directly for these tests.
    public class PlayerTrackerLifecycleTests : PlayerTrackerTestBase
    {
        [Test]
        public void StartPlayerTracking_Fresh_PopulatesTrackedIdsAndResetsInitialStaging()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartPlayerTracking(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetInitialTrackerPlayerIds(tracker));
        }

        [Test]
        public void StartPlayerTracking_Fresh_BroadcastsProcessStartedAndSetsLastPlayerIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartPlayerTracking(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
            Assert.AreEqual(1, tracker.OnTrackingStartedCount);
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.OnTrackingStartedArgs[0]);
        }

        [Test]
        public void StartPlayerTracking_NullPlayerIds_TreatedAsEmpty()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            Assert.DoesNotThrow(() => tracker.StartPlayerTracking(null));

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }

        [Test]
        public void StartPlayerTracking_DuplicateIds_DedupedPreservingFirstOccurrenceOrder()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartPlayerTracking(new[] { "A", "B", "A", "C", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void StartPlayerTracking_SingleId_SkipsDedupPassButStillStarts()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartPlayerTracking(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void StartPlayerTracking_AlreadyRunning_DoesNotMutateInitialStaging()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });
            Assert.AreEqual(1, tracker.OnTrackingStartedCount);

            LogAssert.Expect(LogType.Warning, "[TsProcess] Process is already running.");
            tracker.StartPlayerTracking(new[] { "Z", "Y" });

            Assert.AreEqual(1, tracker.OnTrackingStartedCount, "A second call while running must not restart tracking.");
            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker),
                "The stray call's payload must never reach _trackedPlayerIds.");
            CollectionAssert.AreEqual(new string[0], GetInitialTrackerPlayerIds(tracker),
                "_initialTrackerPlayerIds must stay untouched by a no-op start.");
        }

        [Test]
        public void StartPlayerTracking_UseProcessUpdateTrue_PassesThroughToBaseProcess()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartPlayerTracking(new[] { "A" }, useProcessUpdate: true);

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(tracker, "_useProcessUpdate"));
        }

        [Test]
        public void StopPlayerTracking_Running_BroadcastsStoppedWithCurrentTrackedIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A", "B" });

            tracker.StopPlayerTracking();

            Assert.AreEqual(1, tracker.OnTrackingStoppedCount);
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.OnTrackingStoppedArgs[0]);
            Assert.IsFalse(tracker.IsProcessRunning());
        }

        [Test]
        public void CompletePlayerTracking_Running_BroadcastsCompletedWithCurrentTrackedIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A", "B" });

            tracker.CompletePlayerTracking();

            Assert.AreEqual(1, tracker.OnTrackingCompletedCount);
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.OnTrackingCompletedArgs[0]);
            Assert.IsFalse(tracker.IsProcessRunning());
        }

        [Test]
        public void OnProcessCleanup_NormalStop_ClearsAllFourTrackedStateFields()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A", "B" });

            tracker.StopPlayerTracking();

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], GetInitialTrackerPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
            CollectionAssert.AreEqual(new string[0], tracker.LastAddedPlayerIds);
            CollectionAssert.AreEqual(new string[0], tracker.LastRemovedPlayerIds);
        }

        [Test]
        public void OnProcessCleanup_NormalComplete_ClearsAllFourTrackedStateFields()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A", "B" });

            tracker.CompletePlayerTracking();

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }

        [Test]
        public void OnProcessCleanup_ReentrantRestartFromOnProcessStopped_SkipsClearingTrackedState()
        {
            // Mirrors TsProcessCleanupTests's reentrant-restart test: a subscriber reacting
            // to OnProcessStopped by immediately starting a new run must not have its brand-new
            // _trackedPlayerIds wiped by the old run's cleanup.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });
            tracker.OnProcessStoppedAction = () =>
            {
                SeedAsOwner(tracker);
                tracker.StartPlayerTracking(new[] { "Z" });
            };

            tracker.StopPlayerTracking();

            Assert.IsTrue(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "Z" }, GetTrackedPlayerIds(tracker),
                "The reentrant new process's tracked ids must survive the old process's cleanup.");
        }

        [Test]
        public void FullLifecycle_StartAddRemoveStop_DoesNotThrowAndEndsClean()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            Assert.DoesNotThrow(() =>
            {
                tracker.StartPlayerTracking(new[] { "A" });
                tracker.AddTrackedPlayers(new[] { "B" });
                tracker.RemoveTrackedPlayers(new[] { "A" });
                tracker.StopPlayerTracking();
            });

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
        }

        // LastPlayerIds is assigned before the OnTracking* hook is called in every Notify*
        // method - the following three tests pin that ordering directly, mirroring
        // TsProcessLifecycleTests' RunningStateInsideOnProcessStopped-style pattern.

        [Test]
        public void StartPlayerTracking_LastPlayerIdsAlreadyCurrentInsideOnTrackingStartedHook()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartPlayerTracking(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIdsInsideOnTrackingStarted);
        }

        [Test]
        public void StopPlayerTracking_LastPlayerIdsStillPreClearValueInsideOnTrackingStoppedHook()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A", "B" });

            tracker.StopPlayerTracking();

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIdsInsideOnTrackingStopped,
                "OnTrackingStopped must observe the pre-cleanup tracked set, not the post-cleanup empty one.");
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds,
                "By the time StopPlayerTracking returns, cleanup has already run and cleared it.");
        }

        [Test]
        public void CompletePlayerTracking_LastPlayerIdsStillPreClearValueInsideOnTrackingCompletedHook()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A", "B" });

            tracker.CompletePlayerTracking();

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIdsInsideOnTrackingCompleted);
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }
    }
}
