using System.Collections;
using NUnit.Framework;
using Tsvrc.Core.Generated;
using Tsvrc.Player;
using Tsvrc.Tests.Editor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // Real multi-player Play Mode tests for the branches of PlayerTracker that fundamentally
    // need a real VRCPlayerApi or a live player list (OnPlayerLeft, OnPlayerSuspendChanged,
    // OnOwnerAbandonedProcess) - these can't be reached in Edit Mode: VRCPlayerApi doesn't
    // resolve in that assembly, and neither method has a real player list to scan without
    // ClientSim. Follows the exact harness/verification pattern established by
    // Tests/PlayMode/Core/TsvrcProcess/TsvrcProcessOwnershipHandoverTests.cs: NUnit Assert
    // failures are silent in this project's Play Mode environment (no exception, no results
    // XML), so every test computes its own bool and logs exactly one
    // PLAYMODE_TEST_RESULT marker with PASS/FAIL plus observed values, and ClientSim never
    // organically delivers a networked call to a plain, uncompiled proxy script, so
    // OnPlayerLeft/OnPlayerSuspendChanged are invoked directly with a real VRCPlayerApi
    // rather than relying on ClientSim's own dispatch.
    public class PlayerTrackerAbandonmentTests
    {
        private GameObject _descriptorObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (ClientSimMain.HasInstance())
                ClientSimMain.RemoveInstance();

            ClientSimRuntimeLoader.EndUnityTesting();

            if (_descriptorObject != null)
                Object.DestroyImmediate(_descriptorObject);

            yield return null;
        }

        private void CreateMinimalSceneDescriptor()
        {
            _descriptorObject = new GameObject("__TestSceneDescriptor");
            VRCSceneDescriptor descriptor = _descriptorObject.AddComponent<VRCSceneDescriptor>();

            GameObject spawnObject = new GameObject("__TestSpawn");
            spawnObject.transform.SetParent(_descriptorObject.transform);

            descriptor.spawns = new[] { spawnObject.transform };
        }

        private IEnumerator StartClientSim()
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool("com.vrchat.clientsim.session.accepted_warning", true);
#endif
            CreateMinimalSceneDescriptor();

            ClientSimSettings settings = new ClientSimSettings
            {
                enableClientSim = true,
                spawnPlayer = true,
                deleteEditorOnly = false,
                localPlayerIsMaster = true,
                initializationDelay = 0f,
            };

            ClientSimRuntimeLoader.BeginUnityTesting(settings);
            ClientSimRuntimeLoader.StartClientSim(settings);

            yield return null;
            yield return null;
        }

        private static VRCPlayerApi FindPlayerByName(string name)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            foreach (VRCPlayerApi p in players)
                if (p != null && p.displayName == name) return p;
            return null;
        }

        private static void LogResult(string testName, bool passed, string details)
        {
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);
        }

        // Seeds the tracker as its own process owner using the local ClientSim player's real
        // identity, bypassing SetProcessOwner/Networking.SetOwner entirely - same rationale as
        // TsvrcProcessTestBase.SeedAsOwner, just against a real local player id instead of a
        // synthetic one, since PlayerTracker's own OnPlayerLeft/OnPlayerSuspendChanged/
        // OnOwnerAbandonedProcess guards only ever read _ownerPlayerIdInt/IsProcessOwner(), never
        // Networking.IsOwner directly.
        private static void SeedAsRealLocalOwner(PlayerTrackerTestSubclass tracker, string[] trackedIds)
        {
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            int localIntId = Networking.LocalPlayer.playerId;
            PrivateFieldAccess.SetField(tracker, "_localPlayerId", localId);
            PrivateFieldAccess.SetField(tracker, "_localPlayerIdInt", localIntId);
            PrivateFieldAccess.SetField(tracker, "_ownerId", localId);
            PrivateFieldAccess.SetField(tracker, "_ownerPlayerIdInt", localIntId);
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            PrivateFieldAccess.SetField(tracker, "_trackedPlayerIds", trackedIds);
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_TrackedNonOwnerRemoteLeaves_RemovedFromTrackedIds()
        {
            const string testName = "OnPlayerLeft_TrackedNonOwnerRemoteLeaves_RemovedFromTrackedIds";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_OnPlayerLeft");
            PlayerTrackerTestSubclass tracker = go.AddComponent<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            const string remoteName = "TrackedThenLeaves";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);

            SeedAsRealLocalOwner(tracker, new[] { remoteId });

            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            tracker.OnPlayerLeft(remote);

            string[] trackedAfter = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            int removedCount = tracker.OnTrackingPlayersRemovedCount;

            Object.DestroyImmediate(go);

            bool passed = trackedAfter.Length == 0 && removedCount == 1;
            LogResult(testName, passed, "trackedAfterLength=" + trackedAfter.Length + " removedCount=" + removedCount);
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_ProcessNotRunning_IsNoOpEvenForATrackedPlayer()
        {
            // TsvrcProcess.OnPlayerLeft's own `if (!IsProcessRunning()) return;` runs before it
            // ever dereferences player.playerId - Tests/Editor can't compile a VRCPlayerApi
            // parameter at all (see the Edit Mode PlayerTrackerAbandonmentTests.cs header
            // comment), so this branch is covered here instead, with a real player rather than
            // relying on a null one.
            const string testName = "OnPlayerLeft_ProcessNotRunning_IsNoOpEvenForATrackedPlayer";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_OnPlayerLeft_NotRunning");
            PlayerTrackerTestSubclass tracker = go.AddComponent<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            const string remoteName = "LeavesWhileNotRunning";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);

            // Seed as owner but leave _isRunning at its default false.
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            int localIntId = Networking.LocalPlayer.playerId;
            PrivateFieldAccess.SetField(tracker, "_ownerId", localId);
            PrivateFieldAccess.SetField(tracker, "_ownerPlayerIdInt", localIntId);
            PrivateFieldAccess.SetField(tracker, "_trackedPlayerIds", new[] { remoteId });

            bool threw = false;
            try
            {
                tracker.OnPlayerLeft(remote);
            }
            catch
            {
                threw = true;
            }

            string[] trackedAfter = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            int removedCount = tracker.OnTrackingPlayersRemovedCount;

            Object.DestroyImmediate(go);

            bool passed = !threw && removedCount == 0 && trackedAfter.Length == 1 && trackedAfter[0] == remoteId;
            LogResult(testName, passed, "threw=" + threw + " removedCount=" + removedCount +
                " trackedAfterLength=" + trackedAfter.Length);
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_TrackedNonOwnerRemoteSuspends_RemovedFromTrackedIds()
        {
            const string testName = "OnPlayerSuspendChanged_TrackedNonOwnerRemoteSuspends_RemovedFromTrackedIds";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_OnPlayerSuspendChanged");
            PlayerTrackerTestSubclass tracker = go.AddComponent<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            const string remoteName = "TrackedThenSuspends";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);

            SeedAsRealLocalOwner(tracker, new[] { remoteId });

            remote.GetClientSimPlayer().isSuspended = true;

            tracker.OnPlayerSuspendChanged(remote);

            string[] trackedAfter = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            int removedCount = tracker.OnTrackingPlayersRemovedCount;

            Object.DestroyImmediate(go);

            bool passed = trackedAfter.Length == 0 && removedCount == 1;
            LogResult(testName, passed, "trackedAfterLength=" + trackedAfter.Length + " removedCount=" + removedCount);
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_WakeUp_IsNoOp()
        {
            const string testName = "OnPlayerSuspendChanged_WakeUp_IsNoOp";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_OnPlayerSuspendChanged_WakeUp");
            PlayerTrackerTestSubclass tracker = go.AddComponent<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            const string remoteName = "WakesUp";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);

            SeedAsRealLocalOwner(tracker, new[] { remoteId });

            // isSuspended is false by default (never set true here) - the wakeup case.
            tracker.OnPlayerSuspendChanged(remote);

            string[] trackedAfter = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            int removedCount = tracker.OnTrackingPlayersRemovedCount;

            Object.DestroyImmediate(go);

            bool passed = trackedAfter.Length == 1 && trackedAfter[0] == remoteId && removedCount == 0;
            LogResult(testName, passed, "trackedAfterLength=" + trackedAfter.Length + " removedCount=" + removedCount);
        }

        [UnityTest]
        public IEnumerator OnOwnerAbandonedProcess_RealScan_RemovesDepartedAndSuspended_KeepsActive()
        {
            const string testName = "OnOwnerAbandonedProcess_RealScan_RemovesDepartedAndSuspended_KeepsActive";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_OnOwnerAbandonedProcess");
            PlayerTrackerTestSubclass tracker = go.AddComponent<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            ClientSimMain.SpawnRemotePlayer("WillDepart");
            ClientSimMain.SpawnRemotePlayer("WillSuspend");
            ClientSimMain.SpawnRemotePlayer("StaysActive");
            yield return null;
            yield return null;

            VRCPlayerApi departing = FindPlayerByName("WillDepart");
            VRCPlayerApi suspending = FindPlayerByName("WillSuspend");
            VRCPlayerApi active = FindPlayerByName("StaysActive");
            Assert.IsNotNull(departing, "WillDepart was not spawned.");
            Assert.IsNotNull(suspending, "WillSuspend was not spawned.");
            Assert.IsNotNull(active, "StaysActive was not spawned.");

            string departingId = TsPlayer.GetPlayerID(departing);
            string suspendingId = TsPlayer.GetPlayerID(suspending);
            string activeId = TsPlayer.GetPlayerID(active);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            SeedAsRealLocalOwner(tracker, new[] { departingId, suspendingId, activeId, localId });

            suspending.GetClientSimPlayer().isSuspended = true;
            ClientSimMain.RemovePlayer(departing);
            yield return null;
            yield return null;

            PrivateFieldAccess.InvokeInstance(tracker, "OnOwnerAbandonedProcess");

            string[] trackedAfter = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            bool departedRemoved = System.Array.IndexOf(trackedAfter, departingId) < 0;
            bool suspendedRemoved = System.Array.IndexOf(trackedAfter, suspendingId) < 0;
            bool activeKept = System.Array.IndexOf(trackedAfter, activeId) >= 0;
            bool localKept = System.Array.IndexOf(trackedAfter, localId) >= 0;

            Object.DestroyImmediate(go);

            bool passed = departedRemoved && suspendedRemoved && activeKept && localKept && trackedAfter.Length == 2;
            LogResult(testName, passed, "trackedAfter=[" + string.Join(",", trackedAfter) + "] departedRemoved=" +
                departedRemoved + " suspendedRemoved=" + suspendedRemoved + " activeKept=" + activeKept +
                " localKept=" + localKept);
        }

        [UnityTest]
        public IEnumerator Diagnostic_CallingPlayerIsNullOutsideRealDispatch_EvenUnderClientSim()
        {
            // Permanent regression guard for the assumption both this file and the Edit Mode
            // PlayerTrackerNotifyTests suite depend on: a direct method call carries no real
            // network-call context, so NetworkCalling.CallingPlayer reads null and the Notify*
            // caller-authenticity guard's `caller == null` branch is what actually gets
            // exercised everywhere in this suite. The "accepted" branch (a real caller matching
            // the real owner) is structurally unreachable in this environment: ClientSim's
            // global Udon event broadcast only reaches compiled UdonBehaviour VM instances, never
            // a plain, uncompiled proxy script like this one, so there is no way to make a direct
            // call carry a genuine CallingPlayer identity here.
            const string testName = "Diagnostic_CallingPlayerIsNullOutsideRealDispatch_EvenUnderClientSim";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_CallingPlayerDiagnostic");
            PlayerTrackerTestSubclass tracker = go.AddComponent<PlayerTrackerTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            VRCPlayerApi caller = VRC.SDK3.UdonNetworkCalling.NetworkCalling.CallingPlayer;
            bool callingPlayerIsNull = caller == null;

            // Cross-check via the actual guarded method: a direct call with _isBroadcasting
            // left false must be rejected (no hook fired), consistent with callingPlayerIsNull.
            tracker.NotifyTrackedPlayersAdded(new[] { TsPlayer.GetPlayerID(Networking.LocalPlayer) });
            bool rejectedAsExpected = tracker.OnTrackingPlayersAddedCount == 0;

            Object.DestroyImmediate(go);

            bool passed = callingPlayerIsNull && rejectedAsExpected;
            LogResult(testName, passed, "callingPlayerIsNull=" + callingPlayerIsNull +
                " rejectedAsExpected=" + rejectedAsExpected);
        }
    }
}
