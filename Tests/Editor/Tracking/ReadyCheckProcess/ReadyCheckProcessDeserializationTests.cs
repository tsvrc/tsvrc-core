using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    // Covers OnTrackingDeserialization's derivation of _readyCheckActive from the synced
    // IsProcessRunning() state - the mechanism that gives a late joiner a working SetReady()
    // even though they never received NotifyTrackedPlayersProcessStarted, and corrects a
    // missed stop/complete event on any client.
    public class ReadyCheckProcessDeserializationTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void OnTrackingDeserialization_ProcessRunning_ActivatesReadyCheckFlag()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            Assert.IsFalse(GetReadyCheckActive(tracker));

            tracker.OnDeserialization();

            Assert.IsTrue(GetReadyCheckActive(tracker));
        }

        [Test]
        public void OnTrackingDeserialization_ProcessNotRunning_DeactivatesReadyCheckFlag()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SetReadyCheckActive(tracker, true); // stale true, e.g. from a missed stop event

            tracker.OnDeserialization();

            Assert.IsFalse(GetReadyCheckActive(tracker));
        }

        [Test]
        public void LateJoiner_AfterDeserialization_SetReadyWorksImmediately()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            SetTrackedPlayerIds(tracker, new[] { "TestOwner#" + OwnerPlayerId, "Other#5" });

            tracker.OnDeserialization();
            Assert.IsTrue(GetReadyCheckActive(tracker));

            tracker.SetReady();

            Assert.IsTrue(InvokeIsPlayerReady(tracker, "TestOwner#" + OwnerPlayerId));
            Assert.IsTrue(tracker.IsProcessRunning(), "Other#5 is not ready yet, so the check must still be running.");
        }

        [Test]
        public void OnTrackingDeserialization_CorrectsMissedStopEvent_SetReadyThenNoOps()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SetReadyCheckActive(tracker, true); // stuck true from a missed NotifyTrackedPlayersProcessStopped
            // _isRunning left at its default false, simulating the missed stop having already landed via sync.

            tracker.OnDeserialization();
            Assert.IsFalse(GetReadyCheckActive(tracker));

            SeedAsOwner(tracker);
            Assert.DoesNotThrow(() => tracker.SetReady());
            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }
    }
}
