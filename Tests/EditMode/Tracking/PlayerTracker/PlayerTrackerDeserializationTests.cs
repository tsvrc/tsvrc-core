using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    public class PlayerTrackerDeserializationTests : PlayerTrackerTestBase
    {
        [Test]
        public void OnDeserialization_RefreshesLastPlayerIdsFromSyncedTrackedIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new[] { "A", "B" });

            tracker.OnDeserialization();

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
        }

        [Test]
        public void OnDeserialization_FiresHookAndEvent()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new string[0]);

            tracker.OnDeserialization();

            Assert.AreEqual(1, tracker.OnTrackingDeserializationCount);
        }

        [Test]
        public void OnDeserialization_NotRunningNotOwner_StillRefreshesLastPlayerIds()
        {
            // OnDeserialization has no running/owner guard at all - it's meant to give every
            // client (including one that never started/owns anything locally) an accurate
            // snapshot. Confirm there's genuinely no gate here.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            Assert.IsFalse(tracker.IsProcessRunning());
            SetTrackedPlayerIds(tracker, new[] { "X" });

            tracker.OnDeserialization();

            CollectionAssert.AreEqual(new[] { "X" }, tracker.LastPlayerIds);
        }

        [Test]
        public void OnDeserialization_StaleOverwriteAfterNotify_LastPlayerIdsFollowsSyncedArrayNotDelta()
        {
            // Documents actual behavior: OnDeserialization unconditionally overwrites
            // LastPlayerIds from _trackedPlayerIds, even if a Notify* delta more recently
            // computed a different value. Whichever arrives last wins, by design (no ordering
            // guarantee between serialization and network events per VRChat docs, referenced
            // throughout PlayerTracker.cs's own comments).
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            PrivateFieldAccess.SetField(tracker, "_isBroadcasting", true);
            tracker.NotifyTrackedPlayersAdded(new[] { "A", "B" });
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);

            SetTrackedPlayerIds(tracker, new[] { "A" });
            tracker.OnDeserialization();

            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastPlayerIds);
        }
    }
}
