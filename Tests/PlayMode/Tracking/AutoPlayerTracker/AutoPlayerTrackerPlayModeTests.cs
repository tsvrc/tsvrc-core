using System;
using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.TsProcess;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Tracking.AutoPlayerTracker
{
    // The fresh-start branch of StartAutoTrackingSnapshot() needs a real VRCPlayerApi list
    // (TsPlayer.GetAllPlayerIDs()), and OnPlayerJoined needs a real VRCPlayerApi too. ClientSim
    // does not organically dispatch VRC lifecycle callbacks to a plain UdonSharpBehaviour, so
    // OnPlayerJoined is invoked directly below. AutoPlayerTracker has no dedicated test double -
    // the concrete class is used directly - so tracked-set state is read via
    // PrivateFieldAccess.InvokeInstance("GetTrackedPlayerIds").
    public class AutoPlayerTrackerPlayModeTests : TsProcessPlayModeTestBase
    {
        private static string[] GetTrackedPlayerIds(Tsvrc.Tracking.PlayerTracker tracker)
        {
            return (string[])PrivateFieldAccess.InvokeInstance(tracker, "GetTrackedPlayerIds");
        }

        [UnityTest]
        public IEnumerator StartAutoTracking_FreshStart_TracksAllRealCurrentPlayers()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("First");
            Players.SpawnRemotePlayer("Second");
            yield return null;
            yield return null;
            VRCPlayerApi first = ClientSimPlayerEnvironment.FindPlayerByName("First");
            VRCPlayerApi second = ClientSimPlayerEnvironment.FindPlayerByName("Second");
            Assert.IsNotNull(first, "Setup sanity check: First was not found.");
            Assert.IsNotNull(second, "Setup sanity check: Second was not found.");
            string firstId = first.displayName + "#" + first.playerId;
            string secondId = second.displayName + "#" + second.playerId;
            string localId = Networking.LocalPlayer.displayName + "#" + Networking.LocalPlayer.playerId;

            var tracker = CreateProcess<Tsvrc.Tracking.AutoPlayerTracker>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartAutoTracking();

            string[] trackedIds = GetTrackedPlayerIds(tracker);
            Assert.AreEqual(3, trackedIds.Length,
                "A fresh StartAutoTracking() must snapshot every real player currently in the instance.");
            Assert.IsTrue(Array.IndexOf(trackedIds, localId) >= 0);
            Assert.IsTrue(Array.IndexOf(trackedIds, firstId) >= 0);
            Assert.IsTrue(Array.IndexOf(trackedIds, secondId) >= 0);
        }

        [UnityTest]
        public IEnumerator StartAutoTracking_FreshStartWithAlreadySuspendedPlayer_ExcludesThemFromTrackedSet()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("AlreadySuspended");
            yield return null;
            yield return null;
            VRCPlayerApi suspended = ClientSimPlayerEnvironment.FindPlayerByName("AlreadySuspended");
            Assert.IsNotNull(suspended, "Setup sanity check: AlreadySuspended was not found.");
            string suspendedId = suspended.displayName + "#" + suspended.playerId;
            suspended.GetClientSimPlayer().isSuspended = true;

            var tracker = CreateProcess<Tsvrc.Tracking.AutoPlayerTracker>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartAutoTracking();

            Assert.IsFalse(Array.IndexOf(GetTrackedPlayerIds(tracker), suspendedId) >= 0,
                "A player already suspended at the moment of a fresh StartAutoTracking() must never enter " +
                "the tracked set - PlayerTracker.OnPlayerSuspendChanged only fires on a *transition* to " +
                "suspended, so a player who was already suspended before tracking started would otherwise " +
                "be tracked forever with no event ever available to remove them, since 'waking up' " +
                "(the only transition they can still produce) is deliberately a no-op.");
        }

        [UnityTest]
        public IEnumerator StartAutoTracking_AlreadyRunning_DoesNotRescanRealPlayerList()
        {
            yield return StartClientSim();

            var tracker = CreateProcess<Tsvrc.Tracking.AutoPlayerTracker>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartAutoTracking();
            string[] initialIds = GetTrackedPlayerIds(tracker);

            Players.SpawnRemotePlayer("JoinedAfterStart");
            yield return null;
            yield return null;

            tracker.StartAutoTracking();

            string[] idsAfterSecondCall = GetTrackedPlayerIds(tracker);
            Assert.AreEqual(initialIds.Length, idsAfterSecondCall.Length,
                "StartAutoTracking() while already running must not rescan the real player list.");
        }

        [UnityTest]
        public IEnumerator OnPlayerJoined_RealNewPlayerWhileOwnerAndRunning_AddsThemToTrackedSet()
        {
            yield return StartClientSim();

            var tracker = CreateProcess<Tsvrc.Tracking.AutoPlayerTracker>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartAutoTracking();

            Players.SpawnRemotePlayer("NewJoiner");
            yield return null;
            yield return null;
            VRCPlayerApi newJoiner = ClientSimPlayerEnvironment.FindPlayerByName("NewJoiner");
            Assert.IsNotNull(newJoiner, "Setup sanity check: NewJoiner was not found.");
            string newJoinerId = newJoiner.displayName + "#" + newJoiner.playerId;

            tracker.OnPlayerJoined(newJoiner);

            Assert.IsTrue(Array.IndexOf(GetTrackedPlayerIds(tracker), newJoinerId) >= 0,
                "The real owner must add a genuinely new joining player to the tracked set.");
        }

        [UnityTest]
        public IEnumerator OnPlayerJoined_RealNewPlayerWhileNotOwner_NoAction()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("NamedOwner");
            yield return null;
            yield return null;
            VRCPlayerApi namedOwner = ClientSimPlayerEnvironment.FindPlayerByName("NamedOwner");
            Assert.IsNotNull(namedOwner, "Setup sanity check: NamedOwner was not found.");

            var tracker = CreateProcess<Tsvrc.Tracking.AutoPlayerTracker>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartAutoTracking();
            PrivateFieldAccess.InvokeInstance(tracker, "SetProcessOwner", namedOwner);
            string[] idsBeforeJoin = GetTrackedPlayerIds(tracker);

            Players.SpawnRemotePlayer("NotAddedJoiner");
            yield return null;
            yield return null;
            VRCPlayerApi notAddedJoiner = ClientSimPlayerEnvironment.FindPlayerByName("NotAddedJoiner");
            Assert.IsNotNull(notAddedJoiner, "Setup sanity check: NotAddedJoiner was not found.");

            tracker.OnPlayerJoined(notAddedJoiner);

            Assert.AreEqual(idsBeforeJoin.Length, GetTrackedPlayerIds(tracker).Length,
                "The local (non-owner) client must not act on OnPlayerJoined when a different real player is the named owner.");
        }

        [UnityTest]
        public IEnumerator OnPlayerJoined_InvalidRemovedPlayer_NoAction()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Departed");
            yield return null;
            yield return null;
            VRCPlayerApi departed = ClientSimPlayerEnvironment.FindPlayerByName("Departed");
            Assert.IsNotNull(departed, "Setup sanity check: Departed was not found.");

            var tracker = CreateProcess<Tsvrc.Tracking.AutoPlayerTracker>();
            tracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            tracker.StartAutoTracking();
            int idsBeforeCount = GetTrackedPlayerIds(tracker).Length;

            Players.RemovePlayer(departed);

            Assert.IsFalse(departed.IsValid(),
                "Setup sanity check: a removed ClientSim player must genuinely fail IsValid().");
            tracker.OnPlayerJoined(departed);

            Assert.AreEqual(idsBeforeCount, GetTrackedPlayerIds(tracker).Length,
                "OnPlayerJoined must take no action for a genuinely invalid VRCPlayerApi.");
        }
    }
}
