using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Dedicated, direct coverage of IsTrackedPlayer's own documented contract - elsewhere in
    // the suite it's only ever exercised indirectly as an implementation detail of other
    // methods (BroadcastAddTrackedPlayers's/BroadcastRemoveTrackedPlayers's filters,
    // OnPlayerLeft's/OnPlayerSuspendChanged's guards).
    public class PlayerTrackerIsTrackedPlayerTests : PlayerTrackerTestBase
    {
        [Test]
        public void IsTrackedPlayer_IdInTrackedSet_ReturnsTrue()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new[] { "A", "B" });

            Assert.IsTrue(InvokeIsTrackedPlayer(tracker, "A"));
            Assert.IsTrue(InvokeIsTrackedPlayer(tracker, "B"));
        }

        [Test]
        public void IsTrackedPlayer_IdNotInTrackedSet_ReturnsFalse()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new[] { "A" });

            Assert.IsFalse(InvokeIsTrackedPlayer(tracker, "Z"));
        }

        [Test]
        public void IsTrackedPlayer_EmptyTrackedSet_ReturnsFalse()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new string[0]);

            Assert.IsFalse(InvokeIsTrackedPlayer(tracker, "A"));
        }

        [Test]
        public void IsTrackedPlayer_NullId_ReturnsFalse()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new[] { "A" });

            Assert.IsFalse(InvokeIsTrackedPlayer(tracker, null));
        }

        [Test]
        public void IsTrackedPlayer_ReflectsRawTrackedIdsNotLastPlayerIds()
        {
            // Per its own doc comment: "On non-owner clients this reflects the last deserialized
            // state, which may lag behind network event callbacks." Confirms IsTrackedPlayer
            // reads _trackedPlayerIds directly, not the (possibly further-ahead) LastPlayerIds.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new string[0]);
            PrivateFieldAccess.SetField(tracker, "_isBroadcasting", true);
            tracker.NotifyTrackedPlayersAdded(new[] { "A" });

            // LastPlayerIds now contains "A" (via the Notify delta merge), but the underlying
            // synced field was never touched directly by this call.
            CollectionAssert.Contains(tracker.LastPlayerIds, "A");
            Assert.IsFalse(InvokeIsTrackedPlayer(tracker, "A"),
                "IsTrackedPlayer must reflect _trackedPlayerIds, which NotifyTrackedPlayersAdded never writes to.");
        }
    }
}
