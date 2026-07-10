using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    // Covers input sanitization: every entry point into _trackedPlayerIds/LastPlayerIds/
    // LastAddedPlayerIds/LastRemovedPlayerIds strips null elements and deduplicates its input,
    // independently at each layer (StartPlayerTracking, BroadcastAddTrackedPlayers, and the
    // Notify* receivers, since the latter are public and [NetworkCallable] and can be called
    // directly, bypassing whatever the sender already sanitized).
    public class PlayerTrackerSanitizationTests : PlayerTrackerTestBase
    {
        private static void SetBroadcasting(PlayerTrackerTestSubclass tracker, bool value)
        {
            PrivateFieldAccess.SetField(tracker, "_isBroadcasting", value);
        }

        [Test]
        public void StartPlayerTracking_NullEntry_FilteredOut()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartPlayerTracking(new[] { "A", null, "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.DoesNotContain(GetTrackedPlayerIds(tracker), null);
        }

        [Test]
        public void StartPlayerTracking_OnlyNullEntries_StartsWithEmptyTrackedSet()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            Assert.DoesNotThrow(() => tracker.StartPlayerTracking(new string[] { null, null }));

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void StartPlayerTracking_NullAndDuplicateCombined_BothFiltered()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);

            tracker.StartPlayerTracking(new[] { "A", null, "A", null, "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void BroadcastAddTrackedPlayers_NullEntry_FilteredOut()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            tracker.BroadcastAddTrackedPlayers(new[] { "A", null });

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.DoesNotContain(GetTrackedPlayerIds(tracker), null);
        }

        [Test]
        public void BroadcastAddTrackedPlayers_OnlyNullEntries_NoBroadcastFires()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            Assert.DoesNotThrow(() => tracker.BroadcastAddTrackedPlayers(new string[] { null }));

            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void NotifyTrackedPlayersAdded_DuplicateIdsInDirectCall_DedupedInLastAddedAndLastPlayerIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersAdded(new[] { "A", "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastAddedPlayerIds,
                "A direct call's duplicate ids must not survive into LastAddedPlayerIds.");
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
        }

        [Test]
        public void NotifyTrackedPlayersAdded_NullEntryInDirectCall_FilteredOut()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersAdded(new[] { "A", null });

            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastAddedPlayerIds);
            CollectionAssert.DoesNotContain(tracker.LastPlayerIds, null);
        }

        [Test]
        public void NotifyTrackedPlayersAdded_OnlyNullEntries_IsNoOp()
        {
            // Stripping nulls out of [null] leaves an effectively-empty payload, which must not
            // fire the hook/event at all.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);

            tracker.NotifyTrackedPlayersAdded(new string[] { null });

            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
            CollectionAssert.AreEqual(new string[0], tracker.LastAddedPlayerIds);
        }

        [Test]
        public void NotifyTrackedPlayersRemoved_DuplicateIdsInDirectCall_DedupedInLastRemovedPlayerIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);
            tracker.NotifyTrackedPlayersAdded(new[] { "A", "B" });

            tracker.NotifyTrackedPlayersRemoved(new[] { "A", "A" });

            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastRemovedPlayerIds,
                "A direct call's duplicate ids must not survive into LastRemovedPlayerIds.");
            CollectionAssert.AreEqual(new[] { "B" }, tracker.LastPlayerIds);
        }

        [Test]
        public void NotifyTrackedPlayersRemoved_NullEntryInDirectCall_FilteredOut()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);
            tracker.NotifyTrackedPlayersAdded(new[] { "A" });

            tracker.NotifyTrackedPlayersRemoved(new[] { "A", null });

            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastRemovedPlayerIds);
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }

        [Test]
        public void NotifyTrackedPlayersRemoved_OnlyNullEntries_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetBroadcasting(tracker, true);
            tracker.NotifyTrackedPlayersAdded(new[] { "A" });

            tracker.NotifyTrackedPlayersRemoved(new string[] { null });

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastPlayerIds,
                "An effectively-empty (all-null) removal payload must not touch LastPlayerIds.");
        }
    }
}
