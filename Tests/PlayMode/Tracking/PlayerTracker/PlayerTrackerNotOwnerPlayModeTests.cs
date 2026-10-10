using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using Tsvrc.UdonTestKit;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Tracking.PlayerTracker
{
    // The not-owner paths need a GameObject the local player genuinely doesn't own, which only
    // ClientSim can produce, through Session.SetOwner; Edit Mode always reports the local
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
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            string remoteId = remote.displayName + "#" + remote.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            Session.SetOwner(remote, tracker.gameObject);

            tracker.StartPlayerTracking(new[] { remoteId });

            Assert.IsTrue(Networking.IsOwner(tracker.gameObject),
                "A non-owner's StartPlayerTracking must claim ownership, since the IDs it stages never reach the owner.");
            Assert.IsTrue(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { remoteId }, tracker.LastPlayerIds);
        }

        [UnityTest]
        public IEnumerator BroadcastAddTrackedPlayers_GenuinelyNotOwner_Rejected()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new string[0]);
            Session.SetOwner(remote, tracker.gameObject);
            Assert.IsTrue(tracker.IsProcessRunning(), "Setup sanity check: tracking must still be running.");

            tracker.BroadcastAddTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
        }

        [UnityTest]
        public IEnumerator BroadcastRemoveTrackedPlayers_GenuinelyNotOwner_Rejected()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { "A" });
            Session.SetOwner(remote, tracker.gameObject);
            Assert.IsTrue(tracker.IsProcessRunning(), "Setup sanity check: tracking must still be running.");

            tracker.BroadcastRemoveTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }
    }
}
