using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Tracking.PlayerTracker
{
    // The not-owner paths need a GameObject the local player genuinely doesn't own, which only
    // FakeOwnershipSyncable under ClientSim can produce; Edit Mode always reports the local
    // player as owner.
    public class PlayerTrackerNotOwnerPlayModeTests : ProcessPlayModeTestBase
    {
        private static string[] GetTrackedPlayerIds(Tsvrc.Tracking.PlayerTracker tracker)
        {
            return (string[])PrivateFieldAccess.InvokeInstance(tracker, "GetTrackedPlayerIds");
        }

        [UnityTest]
        public IEnumerator StartPlayerTracking_GenuinelyNotOwner_ClaimsOwnershipAndTracksTheGivenIds()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: RemoteOwner was not found.");
            string remoteId = remote.displayName + "#" + remote.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotOwner(tracker, remote);
            Assert.IsFalse(Networking.IsOwner(tracker.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            tracker.StartPlayerTracking(new[] { remoteId });

            Assert.IsTrue(Networking.IsOwner(tracker.gameObject),
                "A non-owner's StartPlayerTracking must claim ownership, since the IDs it stages never reach the owner.");
            Assert.IsTrue(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { remoteId }, tracker.LastPlayerIds);
        }

        [UnityTest]
        public IEnumerator BroadcastAddTrackedPlayers_GenuinelyNotOwner_Rejected()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: RemoteOwner was not found.");

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new string[0]);
            MakeGenuinelyNotOwner(tracker, remote);
            Assert.IsTrue(tracker.IsProcessRunning(), "Setup sanity check: tracking must still be running.");
            Assert.IsFalse(Networking.IsOwner(tracker.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            tracker.BroadcastAddTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
        }

        [UnityTest]
        public IEnumerator BroadcastRemoveTrackedPlayers_GenuinelyNotOwner_Rejected()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: RemoteOwner was not found.");

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { "A" });
            MakeGenuinelyNotOwner(tracker, remote);
            Assert.IsTrue(tracker.IsProcessRunning(), "Setup sanity check: tracking must still be running.");
            Assert.IsFalse(Networking.IsOwner(tracker.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            tracker.BroadcastRemoveTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }
    }
}
