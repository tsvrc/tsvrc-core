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
    // Real multi-player Play Mode tests for ReadyCheckProcess. SetReady/BroadcastAddReadyPlayer/
    // BroadcastRemoveReadyPlayer are pure int/string logic with no VRCPlayerApi dependency and
    // are already fully covered in Edit Mode; what genuinely needs ClientSim here is
    // OnTrackingPlayersRemoved reacting to a REAL departed/suspended tracked player (inherited
    // from PlayerTracker's OnPlayerLeft/OnOwnerAbandonedProcess machinery, but the ready-check-
    // specific consequences - removal from _readyPlayerIds, and a departure triggering
    // completion for the remaining ready players - are ReadyCheckProcess's own logic) and the
    // real StartProcess/SetProcessOwner path using a genuine cached _localPlayerId rather than
    // a reflection-seeded one.
    //
    // Follows the exact harness/verification pattern established by
    // Tests/PlayMode/Tracking/PlayerTracker/PlayerTrackerAbandonmentTests.cs and
    // Tests/PlayMode/Core/TsvrcProcess/TsvrcProcessOwnershipHandoverTests.cs: NUnit Assert
    // failures are silent in this project's Play Mode environment (no exception, no results
    // XML), so every test computes its own bool and logs exactly one PLAYMODE_TEST_RESULT
    // marker with PASS/FAIL plus observed values. ClientSim never organically delivers a
    // networked call to a plain, uncompiled proxy script, so OnPlayerLeft is invoked directly
    // with a real VRCPlayerApi rather than relying on ClientSim's own dispatch.
    public class ReadyCheckProcessPlayModeTests
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
        // identity, bypassing SetProcessOwner/Networking.SetOwner entirely, plus the
        // ReadyCheckProcess-specific ready-set/active-flag fields - same rationale as
        // PlayerTrackerAbandonmentTests.SeedAsRealLocalOwner, extended for this class's own state.
        private static void SeedAsRealLocalOwner(ReadyCheckProcessTestSubclass tracker, string[] trackedIds, string[] readyIds)
        {
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            int localIntId = Networking.LocalPlayer.playerId;
            PrivateFieldAccess.SetField(tracker, "_localPlayerId", localId);
            PrivateFieldAccess.SetField(tracker, "_localPlayerIdInt", localIntId);
            PrivateFieldAccess.SetField(tracker, "_ownerId", localId);
            PrivateFieldAccess.SetField(tracker, "_ownerPlayerIdInt", localIntId);
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            PrivateFieldAccess.SetField(tracker, "_trackedPlayerIds", trackedIds);
            PrivateFieldAccess.SetField(tracker, "_readyPlayerIds", readyIds);
            PrivateFieldAccess.SetField(tracker, "_readyCheckActive", true);
        }

        [UnityTest]
        public IEnumerator RemoteTrackedPlayerLeaves_WhileNotReady_RemovingThemCompletesReadyCheckForRemainingReadyPlayer()
        {
            const string testName = "RemoteTrackedPlayerLeaves_WhileNotReady_RemovingThemCompletesReadyCheckForRemainingReadyPlayer";

            yield return StartClientSim();

            GameObject go = new GameObject("ReadyCheck_RemoteLeavesNotReady");
            ReadyCheckProcessTestSubclass tracker = go.AddComponent<ReadyCheckProcessTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            const string remoteName = "NotReadyLeaver";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            // Local (already ready) + remote (not ready) are both tracked.
            SeedAsRealLocalOwner(tracker, new[] { localId, remoteId }, new[] { localId });

            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            tracker.OnPlayerLeft(remote);

            // Removing the not-ready remote leaves only the already-ready local player
            // tracked, which completes the check in the same call
            // (OnTrackingPlayersRemoved's own CheckAllPlayersReady). Completion then runs
            // OnProcessCleanup, which clears _trackedPlayerIds/_readyPlayerIds back to empty -
            // there is no way to observe an intermediate "just localId, still running" state,
            // since the removal and the resulting completion collapse into one synchronous call.
            string[] trackedAfter = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            int removedHookCount = tracker.OnTrackingPlayersRemovedCount;
            int completedCount = tracker.OnReadyCheckCompletedCount;
            bool stillRunning = tracker.IsProcessRunning();

            Object.DestroyImmediate(go);

            bool passed = trackedAfter.Length == 0 && removedHookCount == 1 &&
                completedCount == 1 && !stillRunning;
            LogResult(testName, passed, "trackedAfterLength=" + trackedAfter.Length +
                " removedHookCount=" + removedHookCount + " completedCount=" + completedCount +
                " stillRunning=" + stillRunning);
        }

        [UnityTest]
        public IEnumerator RemoteTrackedPlayerLeaves_WhileReady_RemovedFromReadyPlayerIdsWithoutSpuriousCompletion()
        {
            const string testName = "RemoteTrackedPlayerLeaves_WhileReady_RemovedFromReadyPlayerIdsWithoutSpuriousCompletion";

            yield return StartClientSim();

            GameObject go = new GameObject("ReadyCheck_RemoteLeavesReady");
            ReadyCheckProcessTestSubclass tracker = go.AddComponent<ReadyCheckProcessTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            ClientSimMain.SpawnRemotePlayer("ReadyThenLeaves");
            ClientSimMain.SpawnRemotePlayer("StaysNotReady");
            yield return null;
            yield return null;
            VRCPlayerApi remote1 = FindPlayerByName("ReadyThenLeaves");
            VRCPlayerApi remote2 = FindPlayerByName("StaysNotReady");
            Assert.IsNotNull(remote1, "ReadyThenLeaves was not spawned.");
            Assert.IsNotNull(remote2, "StaysNotReady was not spawned.");
            string remote1Id = TsPlayer.GetPlayerID(remote1);
            string remote2Id = TsPlayer.GetPlayerID(remote2);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            // remote1 is ready; local and remote2 are not.
            SeedAsRealLocalOwner(tracker, new[] { localId, remote1Id, remote2Id }, new[] { remote1Id });

            ClientSimMain.RemovePlayer(remote1);
            yield return null;
            yield return null;

            tracker.OnPlayerLeft(remote1);

            string[] readyAfter = PrivateFieldAccess.GetField<string[]>(tracker, "_readyPlayerIds");
            bool readyStillContainsDeparted = System.Array.IndexOf(readyAfter, remote1Id) >= 0;
            int completedCount = tracker.OnReadyCheckCompletedCount;

            Object.DestroyImmediate(go);

            bool passed = !readyStillContainsDeparted && readyAfter.Length == 0 && completedCount == 0;
            LogResult(testName, passed, "readyAfterLength=" + readyAfter.Length +
                " readyStillContainsDeparted=" + readyStillContainsDeparted + " completedCount=" + completedCount);
        }

        [UnityTest]
        public IEnumerator OnOwnerAbandonedProcess_BaseScanCompletesSynchronously_ReentrantRestartLeavesReadyCheckActiveCorrect()
        {
            // Compounds two mechanisms that can only both be exercised together with a real,
            // non-empty tracked set (Edit Mode can't reach PlayerTracker.OnOwnerAbandonedProcess's
            // own GetAllPlayers()-dependent scan at all unless _trackedPlayerIds is empty):
            // the base class's own abandonment scan removing a departed, never-ready tracked
            // player can, through OnTrackingPlayersRemoved's own CheckAllPlayersReady() call,
            // complete the ready check SYNCHRONOUSLY, DURING base.OnOwnerAbandonedProcess() -
            // i.e. before this class's own _readyCheckActive correction (the line after the
            // base call) ever runs. If a subscriber to that completion restarts the check
            // inline, this test confirms _readyCheckActive still ends up correctly true for
            // the NEW run, not clobbered by the post-base-call correction line.
            const string testName = "OnOwnerAbandonedProcess_BaseScanCompletesSynchronously_ReentrantRestartLeavesReadyCheckActiveCorrect";

            yield return StartClientSim();

            GameObject go = new GameObject("ReadyCheck_AbandonmentSyncCompleteRestart");
            ReadyCheckProcessTestSubclass tracker = go.AddComponent<ReadyCheckProcessTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            ClientSimMain.SpawnRemotePlayer("DepartsNeverReady");
            yield return null;
            yield return null;
            VRCPlayerApi departing = FindPlayerByName("DepartsNeverReady");
            Assert.IsNotNull(departing, "DepartsNeverReady was not spawned.");
            string departingId = TsPlayer.GetPlayerID(departing);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            // local is already ready; the remote (never ready) is about to be scanned out by
            // the base class's own abandonment logic, which should complete the check for the
            // sole remaining (ready) player - local - synchronously inside the base call.
            SeedAsRealLocalOwner(tracker, new[] { localId, departingId }, new[] { localId });

            tracker.OnReadyCheckCompletedAction = () => tracker.StartReadyCheck(new[] { "NewRun#1" });

            ClientSimMain.RemovePlayer(departing);
            yield return null;
            yield return null;

            PrivateFieldAccess.InvokeInstance(tracker, "OnOwnerAbandonedProcess");

            bool readyCheckActiveAfter = PrivateFieldAccess.GetField<bool>(tracker, "_readyCheckActive");
            string[] trackedAfter = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            int completedCount = tracker.OnReadyCheckCompletedCount;
            int startedCount = tracker.OnReadyCheckStartedCount;

            Object.DestroyImmediate(go);

            bool passed = completedCount == 1 && startedCount == 1 && readyCheckActiveAfter &&
                trackedAfter.Length == 1 && trackedAfter[0] == "NewRun#1";
            LogResult(testName, passed, "completedCount=" + completedCount + " startedCount=" + startedCount +
                " readyCheckActiveAfter=" + readyCheckActiveAfter + " trackedAfter=[" + string.Join(",", trackedAfter) + "]");
        }

        [UnityTest]
        public IEnumerator SetReady_RealLocalOwner_UsesRealCachedLocalPlayerIdAndClaimsRealOwnership()
        {
            const string testName = "SetReady_RealLocalOwner_UsesRealCachedLocalPlayerIdAndClaimsRealOwnership";

            yield return StartClientSim();

            GameObject go = new GameObject("ReadyCheck_RealOwnerSetReady");
            ReadyCheckProcessTestSubclass tracker = go.AddComponent<ReadyCheckProcessTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            // Real StartProcess -> SetProcessOwner(Networking.LocalPlayer), no reflection seeding.
            tracker.StartReadyCheck(new[] { localId });
            yield return null;

            bool isUnityOwner = Networking.IsOwner(go);

            tracker.SetReady();

            int completedCount = tracker.OnReadyCheckCompletedCount;
            bool stillRunning = tracker.IsProcessRunning();

            Object.DestroyImmediate(go);

            bool passed = isUnityOwner && completedCount == 1 && !stillRunning;
            LogResult(testName, passed, "isUnityOwner=" + isUnityOwner +
                " completedCount=" + completedCount + " stillRunning=" + stillRunning);
        }

        [UnityTest]
        public IEnumerator OnOwnerAbandonedProcess_RealTakeover_CorrectsStaleReadyCheckActiveAndNewOwnerCanReadyUp()
        {
            // A client that takes over an abandoned, still-running ready check before it had
            // ever received NotifyTrackedPlayersProcessStarted or a corrective deserialization
            // is left with a stale _readyCheckActive=false, corrected by
            // ReadyCheckProcess.OnOwnerAbandonedProcess - simulated here directly, since
            // ClientSim has only one real Udon execution context and can't organically produce
            // that race. The real value ClientSim adds is TsPlayer.GetAllPlayers() returning a
            // genuine player list for PlayerTracker's own abandonment scan (base.OnOwnerAbandonedProcess),
            // which is not reachable at all in Edit Mode with a non-empty tracked set.
            const string testName = "OnOwnerAbandonedProcess_RealTakeover_CorrectsStaleReadyCheckActiveAndNewOwnerCanReadyUp";

            yield return StartClientSim();

            GameObject go = new GameObject("ReadyCheck_OwnerAbandonedRealScan");
            ReadyCheckProcessTestSubclass tracker = go.AddComponent<ReadyCheckProcessTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            ClientSimMain.SpawnRemotePlayer("StaysActive");
            yield return null;
            yield return null;
            VRCPlayerApi activeRemote = FindPlayerByName("StaysActive");
            Assert.IsNotNull(activeRemote, "StaysActive was not spawned.");
            string activeRemoteId = TsPlayer.GetPlayerID(activeRemote);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            // Seed as the just-took-over owner with the stale flag from before takeover, and a
            // real, non-empty tracked set so PlayerTracker's own scan runs its full logic.
            PrivateFieldAccess.SetField(tracker, "_localPlayerId", localId);
            PrivateFieldAccess.SetField(tracker, "_localPlayerIdInt", Networking.LocalPlayer.playerId);
            PrivateFieldAccess.SetField(tracker, "_ownerId", localId);
            PrivateFieldAccess.SetField(tracker, "_ownerPlayerIdInt", Networking.LocalPlayer.playerId);
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            PrivateFieldAccess.SetField(tracker, "_trackedPlayerIds", new[] { localId, activeRemoteId });
            PrivateFieldAccess.SetField(tracker, "_readyPlayerIds", new string[0]);
            PrivateFieldAccess.SetField(tracker, "_readyCheckActive", false); // stale, as if takeover happened before any sync arrived

            PrivateFieldAccess.InvokeInstance(tracker, "OnOwnerAbandonedProcess");

            bool readyCheckActiveAfter = PrivateFieldAccess.GetField<bool>(tracker, "_readyCheckActive");

            tracker.SetReady();
            bool localMarkedReady = PrivateFieldAccess.InvokeInstance(tracker, "IsPlayerReady", localId) is bool b && b;

            Object.DestroyImmediate(go);

            bool passed = readyCheckActiveAfter && localMarkedReady;
            LogResult(testName, passed, "readyCheckActiveAfter=" + readyCheckActiveAfter +
                " localMarkedReady=" + localMarkedReady);
        }

        [UnityTest]
        public IEnumerator Diagnostic_BroadcastAddReadyPlayer_CallingPlayerIsNullOutsideRealDispatch_EvenUnderClientSim()
        {
            // Permanent regression guard, mirroring PlayerTrackerAbandonmentTests's identical
            // diagnostic: applied here specifically to ReadyCheckProcess's own [NetworkCallable]
            // receivers rather than PlayerTracker's. A direct call for an id that is NOT the
            // real local caller still succeeds, confirming the self-only guard is always
            // skipped in this environment (no way to construct a real mismatched CallingPlayer).
            const string testName = "Diagnostic_BroadcastAddReadyPlayer_CallingPlayerIsNullOutsideRealDispatch_EvenUnderClientSim";

            yield return StartClientSim();

            GameObject go = new GameObject("ReadyCheck_CallingPlayerDiagnostic");
            ReadyCheckProcessTestSubclass tracker = go.AddComponent<ReadyCheckProcessTestSubclass>();
            tracker.TsConstruct((TsvrcRoot)null);

            const string remoteName = "SomeoneElse";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            SeedAsRealLocalOwner(tracker, new[] { localId, remoteId }, new string[0]);

            VRCPlayerApi caller = VRC.SDK3.UdonNetworkCalling.NetworkCalling.CallingPlayer;
            bool callingPlayerIsNull = caller == null;

            // Direct call marking the REMOTE player ready, from the local (owner) instance -
            // a genuine mismatch if CallingPlayer were real, but it never is here.
            tracker.BroadcastAddReadyPlayer(remoteId);
            bool remoteMarkedReady = System.Array.IndexOf(
                PrivateFieldAccess.GetField<string[]>(tracker, "_readyPlayerIds"), remoteId) >= 0;

            Object.DestroyImmediate(go);

            bool passed = callingPlayerIsNull && remoteMarkedReady;
            LogResult(testName, passed, "callingPlayerIsNull=" + callingPlayerIsNull +
                " remoteMarkedReady=" + remoteMarkedReady);
        }
    }
}
