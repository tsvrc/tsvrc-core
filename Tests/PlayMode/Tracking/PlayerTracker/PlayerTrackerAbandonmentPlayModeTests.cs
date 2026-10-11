using System;
using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using Tsvrc.UdonTestKit;
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
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("Tracked");
            string remoteId = remote.displayName + "#" + remote.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { remoteId });

            Session.RemovePlayer(remote);
            tracker.OnPlayerLeft(remote);

            Assert.IsFalse(Array.IndexOf(tracker.LastPlayerIds, remoteId) >= 0,
                "The departed player must be removed from LastPlayerIds by a real BroadcastRemoveTrackedPlayers call.");
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RealNonTrackedRemotePlayerLeaves_NoBroadcast()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("NotTracked");

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(Array.Empty<string>());

            Session.RemovePlayer(remote);
            tracker.OnPlayerLeft(remote);

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_GenuinelyNotNamedOwner_NoBroadcast()
        {
            yield return Session.Start();
            VRCPlayerApi namedOwner = Session.SpawnRemotePlayer("NamedOwner");
            VRCPlayerApi departing = Session.SpawnRemotePlayer("Departing");
            string departingId = departing.displayName + "#" + departing.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { departingId });
            Session.SetOwner(namedOwner, tracker.gameObject);

            Session.RemovePlayer(departing);
            tracker.OnPlayerLeft(departing);

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount,
                "The local (non-owner) client must not act on OnPlayerLeft when a different real player is the named owner.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_RealTrackedRemotePlayerSuspends_BroadcastsRemoval()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("Suspending");
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
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("WakingUp");
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
            yield return Session.Start();
            VRCPlayerApi departed = Session.SpawnRemotePlayer("Departed");
            VRCPlayerApi suspended = Session.SpawnRemotePlayer("Suspended");
            VRCPlayerApi stillActive = Session.SpawnRemotePlayer("StillActive");
            string departedId = departed.displayName + "#" + departed.playerId;
            string suspendedId = suspended.displayName + "#" + suspended.playerId;
            string stillActiveId = stillActive.displayName + "#" + stillActive.playerId;

            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartPlayerTracking(new[] { departedId, suspendedId, stillActiveId });

            suspended.GetClientSimPlayer().isSuspended = true;
            Session.RemovePlayer(departed);

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
            yield return Session.Start();
            VRCPlayerApi active1 = Session.SpawnRemotePlayer("Active1");
            VRCPlayerApi active2 = Session.SpawnRemotePlayer("Active2");
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
