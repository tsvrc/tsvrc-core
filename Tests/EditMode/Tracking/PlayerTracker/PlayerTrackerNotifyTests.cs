using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Direct tests of the five Notify* [NetworkCallable] receivers: null/empty guards, the
    // _isBroadcasting bypass (the path every normal owner-driven call actually takes), the
    // caller-authenticity rejection path, sanitization of the incoming array, and the
    // added/removed delta-dedup logic.
    public class PlayerTrackerNotifyTests : PlayerTrackerTestBase
    {
        private static void SetBroadcasting(PlayerTrackerTestSubclass tracker, bool value)
        {
            PrivateFieldAccess.SetField(tracker, "_isBroadcasting", value);
        }

        [Test]
        public void NotifyTrackedPlayersProcessStarted_Null_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessStarted(null);

            Assert.AreEqual(0, tracker.OnTrackingStartedCount);
        }

        [Test]
        public void NotifyTrackedPlayersProcessStarted_Broadcasting_SetsLastPlayerIdsAndFiresHookAndEvent()
        {
            // TsEmit's actual pub/sub delivery mechanics are already fully covered by
            // TsvrcBehaviourTests; this only needs to confirm the call completes without
            // throwing (no subscribers registered) and the hook/state side effects land.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            Assert.DoesNotThrow(() => tracker.NotifyTrackedPlayersProcessStarted(new[] { "A", "B" }));

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
            Assert.AreEqual(1, tracker.OnTrackingStartedCount);
        }

        [Test]
        public void NotifyTrackedPlayersProcessStopped_Null_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessStopped(null);

            Assert.AreEqual(0, tracker.OnTrackingStoppedCount);
        }

        [Test]
        public void NotifyTrackedPlayersProcessStopped_Broadcasting_SetsLastPlayerIdsAndFiresHook()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessStopped(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastPlayerIds);
            Assert.AreEqual(1, tracker.OnTrackingStoppedCount);
        }

        [Test]
        public void NotifyTrackedPlayersProcessCompleted_Null_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessCompleted(null);

            Assert.AreEqual(0, tracker.OnTrackingCompletedCount);
        }

        [Test]
        public void NotifyTrackedPlayersProcessCompleted_Broadcasting_SetsLastPlayerIdsAndFiresHook()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessCompleted(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastPlayerIds);
            Assert.AreEqual(1, tracker.OnTrackingCompletedCount);
        }

        [Test]
        public void NotifyTrackedPlayersAdded_Null_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersAdded(null);

            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void NotifyTrackedPlayersAdded_Empty_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersAdded(new string[0]);

            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount,
                "An explicitly empty (non-null) array must not fire the hook/event.");
        }

        [Test]
        public void NotifyTrackedPlayersAdded_Broadcasting_SetsLastAddedAndAppendsToLastPlayerIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersAdded(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastAddedPlayerIds);
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
            Assert.AreEqual(1, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void NotifyTrackedPlayersAdded_EntryAlreadyInLastPlayerIds_DedupedFromDelta()
        {
            // Simulates the documented "serialization arrived before this event" race:
            // LastPlayerIds already contains "A" (e.g. via OnDeserialization) by the time this
            // notification arrives with "A" again alongside a genuinely new "B".
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);
            SetTrackedPlayerIds(tracker, new[] { "A" });
            tracker.OnDeserialization();
            Assert.AreEqual(1, tracker.OnTrackingDeserializationCount);

            tracker.NotifyTrackedPlayersAdded(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds,
                "The already-present 'A' must not be duplicated when merged into LastPlayerIds.");
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastAddedPlayerIds,
                "LastAddedPlayerIds itself still reports the full broadcast payload as received.");
        }

        [Test]
        public void NotifyTrackedPlayersRemoved_Null_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersRemoved(null);

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }

        [Test]
        public void NotifyTrackedPlayersRemoved_Empty_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersRemoved(new string[0]);

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }

        [Test]
        public void NotifyTrackedPlayersRemoved_Broadcasting_SetsLastRemovedAndTrimsLastPlayerIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);
            tracker.NotifyTrackedPlayersAdded(new[] { "A", "B" });

            tracker.NotifyTrackedPlayersRemoved(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastRemovedPlayerIds);
            CollectionAssert.AreEqual(new[] { "B" }, tracker.LastPlayerIds);
            Assert.AreEqual(1, tracker.OnTrackingPlayersRemovedCount);
        }

        [Test]
        public void NotifyTrackedPlayersAdded_DirectCallWithoutBroadcastingFlag_RejectedByCallerGuard()
        {
            // NetworkCalling.CallingPlayer reads null outside a real dispatch; the guard's
            // `caller == null` check short-circuits before Networking.GetOwner is ever touched,
            // so this is safe to call directly without ClientSim/Play Mode.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            // _isBroadcasting left at its default false.

            Assert.DoesNotThrow(() => tracker.NotifyTrackedPlayersAdded(new[] { "A" }));

            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount,
                "A direct call outside any real broadcast/dispatch context must be rejected.");
        }

        [Test]
        public void NotifyTrackedPlayersProcessStarted_DirectCallWithoutBroadcastingFlag_RejectedByCallerGuard()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();

            Assert.DoesNotThrow(() => tracker.NotifyTrackedPlayersProcessStarted(new[] { "A" }));

            Assert.AreEqual(0, tracker.OnTrackingStartedCount);
        }

        [Test]
        public void NotifyTrackedPlayersProcessStopped_DirectCallWithoutBroadcastingFlag_RejectedByCallerGuard()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();

            Assert.DoesNotThrow(() => tracker.NotifyTrackedPlayersProcessStopped(new[] { "A" }));

            Assert.AreEqual(0, tracker.OnTrackingStoppedCount);
        }

        [Test]
        public void NotifyTrackedPlayersProcessCompleted_DirectCallWithoutBroadcastingFlag_RejectedByCallerGuard()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();

            Assert.DoesNotThrow(() => tracker.NotifyTrackedPlayersProcessCompleted(new[] { "A" }));

            Assert.AreEqual(0, tracker.OnTrackingCompletedCount);
        }

        [Test]
        public void NotifyTrackedPlayersRemoved_DirectCallWithoutBroadcastingFlag_RejectedByCallerGuard()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);
            tracker.NotifyTrackedPlayersAdded(new[] { "A" });
            SetBroadcasting(tracker, false);

            Assert.DoesNotThrow(() => tracker.NotifyTrackedPlayersRemoved(new[] { "A" }));

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastPlayerIds,
                "A rejected direct call must not remove anything from LastPlayerIds.");
        }

        [Test]
        public void NotifyTrackedPlayersProcessStarted_DuplicateAndNullInDirectCall_SanitizedInLastPlayerIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessStarted(new[] { "A", "A", null, "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds,
                "Duplicates and nulls in a direct call must not survive into LastPlayerIds.");
        }

        [Test]
        public void NotifyTrackedPlayersProcessStarted_OnlyNullEntries_LeavesLastPlayerIdsEmptyButStillFires()
        {
            // Unlike Added/Removed, an empty (post-sanitize) payload is a legitimate value here -
            // the hook must still fire, just with an empty array.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessStarted(new string[] { null });

            Assert.AreEqual(1, tracker.OnTrackingStartedCount);
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }

        [Test]
        public void NotifyTrackedPlayersProcessStopped_DuplicateAndNullInDirectCall_SanitizedInLastPlayerIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessStopped(new[] { "A", "A", null, "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
        }

        [Test]
        public void NotifyTrackedPlayersProcessCompleted_DuplicateAndNullInDirectCall_SanitizedInLastPlayerIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessCompleted(new[] { "A", "A", null, "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
        }

        [Test]
        public void NotifyTrackedPlayersProcessStopped_OnlyNullEntries_LeavesLastPlayerIdsEmptyButStillFires()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessStopped(new string[] { null });

            Assert.AreEqual(1, tracker.OnTrackingStoppedCount);
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }

        [Test]
        public void NotifyTrackedPlayersProcessCompleted_OnlyNullEntries_LeavesLastPlayerIdsEmptyButStillFires()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersProcessCompleted(new string[] { null });

            Assert.AreEqual(1, tracker.OnTrackingCompletedCount);
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }
    }
}
