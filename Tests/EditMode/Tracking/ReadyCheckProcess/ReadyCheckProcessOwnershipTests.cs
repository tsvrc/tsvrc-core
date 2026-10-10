using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers ReadyCheckProcess's own OnBecameProcessOwner override: correcting
    // _readyCheckActive to match IsProcessRunning() on takeover, the same correction
    // OnTrackingDeserialization already applies for late joiners. Without this, a client
    // that takes over an abandoned, still-running ready check before ever having received
    // NotifyTrackedPlayersProcessStarted or a corrective deserialization would be stuck with
    // _readyCheckActive=false forever (OnDeserialization never fires for the client whose
    // own RequestSerialization produced the packet), unable to ever call SetReady() for itself.
    //
    // PlayerTracker.OnBecameProcessOwner's own scan calls TsPlayer.GetAllPlayers(), which
    // needs a live VRCPlayerApi list and only returns early (Edit-Mode-safe) when
    // _trackedPlayerIds is empty. Every test here keeps the tracked set empty for that reason;
    // the real, non-empty-tracked-set path is covered in Play Mode.
    public class ReadyCheckProcessOwnershipTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void OnBecameProcessOwner_ReadyCheckActiveWasStaleFalse_CorrectedToTrueWhileRunning()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            SetReadyCheckActive(tracker, false);
            SetTrackedPlayerIds(tracker, new string[0]);

            PrivateFieldAccess.InvokeInstance(tracker, "OnBecameProcessOwner");

            Assert.IsTrue(GetReadyCheckActive(tracker));
        }

        [Test]
        public void OnBecameProcessOwner_ReadyCheckActiveAlreadyTrue_StaysTrue()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            SetReadyCheckActive(tracker, true);
            SetTrackedPlayerIds(tracker, new string[0]);

            PrivateFieldAccess.InvokeInstance(tracker, "OnBecameProcessOwner");

            Assert.IsTrue(GetReadyCheckActive(tracker));
        }

        [Test]
        public void OnBecameProcessOwner_NotRunning_SetsReadyCheckActiveFalse()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            // _isRunning left at its default false.
            SetReadyCheckActive(tracker, true);
            SetTrackedPlayerIds(tracker, new string[0]);

            PrivateFieldAccess.InvokeInstance(tracker, "OnBecameProcessOwner");

            Assert.IsFalse(GetReadyCheckActive(tracker));
        }

        [Test]
        public void OnBecameProcessOwner_ReadyCheckActiveCorrected_SetReadyThenWorksForTheNewOwner()
        {
            // End-to-end pin: after takeover corrects the stale flag, the new owner's own
            // SetReady() call must actually work.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            SetReadyCheckActive(tracker, false); // stale, as if takeover happened before any sync arrived
            SetTrackedPlayerIds(tracker, new string[0]); // empty, to stay Edit-Mode-safe per the base class's scan

            PrivateFieldAccess.InvokeInstance(tracker, "OnBecameProcessOwner");
            Assert.IsTrue(GetReadyCheckActive(tracker));

            // Now track the local (new owner) player and confirm SetReady no longer no-ops.
            SetTrackedPlayerIds(tracker, new[] { "TestOwner#" + OwnerPlayerId, "Other#1" });
            tracker.SetReady();

            Assert.IsTrue(InvokeIsPlayerReady(tracker, "TestOwner#" + OwnerPlayerId),
                "SetReady() must take effect once _readyCheckActive has been corrected.");
        }
    }
}
