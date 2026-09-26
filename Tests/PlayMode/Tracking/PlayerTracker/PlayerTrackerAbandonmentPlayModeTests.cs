using System;
using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Tracking.PlayerTracker
{
    // OnPlayerLeft/OnPlayerSuspendChanged need a real VRCPlayerApi, and OnBecameProcessOwner's
    // non-empty-tracked-list scan needs a live player list. ClientSim never organically dispatches
    // VRC lifecycle callbacks to a plain UdonSharpBehaviour, so these tests call the overrides
    // directly instead.
    public class PlayerTrackerAbandonmentPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator OnPlayerLeft_RealTrackedRemotePlayerLeaves_BroadcastsRemoval()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Tracked");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("Tracked");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");
            string remoteId = remote.displayName + "#" + remote.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { remoteId });

            Players.RemovePlayer(remote);
            tracker.OnPlayerLeft(remote);

            Assert.IsFalse(Array.IndexOf(tracker.LastPlayerIds, remoteId) >= 0,
                "The departed player must be removed from LastPlayerIds by a real BroadcastRemoveTrackedPlayers call.");
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RealNonTrackedRemotePlayerLeaves_NoBroadcast()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("NotTracked");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("NotTracked");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(Array.Empty<string>());

            Players.RemovePlayer(remote);
            tracker.OnPlayerLeft(remote);

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_GenuinelyNotNamedOwner_NoBroadcast()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("NamedOwner");
            Players.SpawnRemotePlayer("Departing");
            yield return null;
            yield return null;
            VRCPlayerApi namedOwner = ClientSimPlayerEnvironment.FindPlayerByName("NamedOwner");
            VRCPlayerApi departing = ClientSimPlayerEnvironment.FindPlayerByName("Departing");
            Assert.IsNotNull(namedOwner, "Setup sanity check: NamedOwner was not found.");
            Assert.IsNotNull(departing, "Setup sanity check: Departing was not found.");
            string departingId = departing.displayName + "#" + departing.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { departingId });
            PrivateFieldAccess.InvokeInstance(tracker, "SetProcessOwner", namedOwner);

            Players.RemovePlayer(departing);
            tracker.OnPlayerLeft(departing);

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount,
                "The local (non-owner) client must not act on OnPlayerLeft when a different real player is the named owner.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_RealTrackedRemotePlayerSuspends_BroadcastsRemoval()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Suspending");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("Suspending");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");
            string remoteId = remote.displayName + "#" + remote.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { remoteId });

            remote.GetClientSimPlayer().isSuspended = true;
            tracker.OnPlayerSuspendChanged(remote);

            Assert.IsFalse(Array.IndexOf(tracker.LastPlayerIds, remoteId) >= 0,
                "A genuinely suspended tracked player must be removed by a real BroadcastRemoveTrackedPlayers call.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_RealTrackedRemotePlayerWakesUp_NoAction()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("WakingUp");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("WakingUp");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");
            string remoteId = remote.displayName + "#" + remote.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { remoteId });

            remote.GetClientSimPlayer().isSuspended = false;
            tracker.OnPlayerSuspendChanged(remote);

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount,
                "A wakeup (isSuspended=false) must never remove the player - only the suspend event does.");
        }

        [UnityTest]
        public IEnumerator OnBecameProcessOwner_RealMixOfDepartedSuspendedAndActiveTrackedPlayers_RemovesOnlyInactiveOnes()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Departed");
            Players.SpawnRemotePlayer("Suspended");
            Players.SpawnRemotePlayer("StillActive");
            yield return null;
            yield return null;
            VRCPlayerApi departed = ClientSimPlayerEnvironment.FindPlayerByName("Departed");
            VRCPlayerApi suspended = ClientSimPlayerEnvironment.FindPlayerByName("Suspended");
            VRCPlayerApi stillActive = ClientSimPlayerEnvironment.FindPlayerByName("StillActive");
            Assert.IsNotNull(departed, "Setup sanity check: Departed was not found.");
            Assert.IsNotNull(suspended, "Setup sanity check: Suspended was not found.");
            Assert.IsNotNull(stillActive, "Setup sanity check: StillActive was not found.");
            string departedId = departed.displayName + "#" + departed.playerId;
            string suspendedId = suspended.displayName + "#" + suspended.playerId;
            string stillActiveId = stillActive.displayName + "#" + stillActive.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { departedId, suspendedId, stillActiveId });

            suspended.GetClientSimPlayer().isSuspended = true;
            Players.RemovePlayer(departed);

            PrivateFieldAccess.InvokeInstance(tracker, "OnBecameProcessOwner");

            Assert.IsFalse(Array.IndexOf(tracker.LastPlayerIds, departedId) >= 0,
                "The departed player must be removed by the real GetAllPlayers() scan.");
            Assert.IsFalse(Array.IndexOf(tracker.LastPlayerIds, suspendedId) >= 0,
                "The genuinely suspended player must be removed by the real GetAllPlayers() scan.");
            Assert.IsTrue(Array.IndexOf(tracker.LastPlayerIds, stillActiveId) >= 0,
                "A still-active tracked player must not be removed.");
        }

        [UnityTest]
        public IEnumerator OnBecameProcessOwner_RealAllTrackedPlayersStillActive_NoRemoval()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Active1");
            Players.SpawnRemotePlayer("Active2");
            yield return null;
            yield return null;
            VRCPlayerApi active1 = ClientSimPlayerEnvironment.FindPlayerByName("Active1");
            VRCPlayerApi active2 = ClientSimPlayerEnvironment.FindPlayerByName("Active2");
            Assert.IsNotNull(active1, "Setup sanity check: Active1 was not found.");
            Assert.IsNotNull(active2, "Setup sanity check: Active2 was not found.");
            string active1Id = active1.displayName + "#" + active1.playerId;
            string active2Id = active2.displayName + "#" + active2.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { active1Id, active2Id });

            PrivateFieldAccess.InvokeInstance(tracker, "OnBecameProcessOwner");

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount,
                "No removal must happen when every tracked player is still active and not suspended.");
        }
    }
}
