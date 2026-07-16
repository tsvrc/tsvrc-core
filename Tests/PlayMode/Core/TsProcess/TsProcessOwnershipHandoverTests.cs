using System.Collections;
using NUnit.Framework;
using Tsvrc.Core;
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
    // Real multi-player Play Mode tests for TsProcess's ownership-handover logic,
    // using genuine ClientSim VRCPlayerApi/Networking behavior instead of the
    // reflection-seeded owner shortcut the Edit Mode suite uses.
    //
    // NUnit Assert.X failures inside a [UnityTest] are silent in this project's Play
    // Mode environment: no exception, no stack trace, nothing in the log. The
    // affected test's coroutine simply ends and the run moves on, and the NUnit
    // results XML is never written for a Play Mode run either. Every test below
    // therefore does its own manual pass/fail check and always logs exactly one
    // PLAYMODE_TEST_RESULT marker with PASS or FAIL plus the observed values, never
    // relying on Assert to signal failure. Assert.IsNotNull is still used for early
    // "did ClientSim even spawn this player" sanity checks, where a manual check
    // would just be more verbose for no benefit — never for the actual
    // scenario-under-test assertion.
    //
    // Networking.SetOwner to a ClientSim remote player never actually transfers
    // Unity-level ownership: ClientSim runs only one real Udon execution context (the
    // local player's) per Editor session, so Networking.IsOwner(gameObject) is always
    // true locally, regardless of which player SetOwner targeted.
    public class TsProcessOwnershipHandoverTests
    {
        private GameObject _descriptorObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (ClientSimMain.HasInstance())
                ClientSimMain.RemoveInstance();

            ClientSimRuntimeLoader.EndUnityTesting();

            if (_descriptorObject != null)
                UnityEngine.Object.DestroyImmediate(_descriptorObject);

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

        [UnityTest]
        public IEnumerator Diagnostic_SetOwnerToRemotePlayer_DoesNotActuallyTransferOwnership()
        {
            const string testName = "Diagnostic_SetOwnerToRemotePlayer_DoesNotActuallyTransferOwnership";

            yield return StartClientSim();

            GameObject probeObject = new GameObject("DiagnosticOwnerObject");
            probeObject.AddComponent<TsProcess>();

            const string remoteName = "RemoteOwner";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;

            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");

            Networking.SetOwner(remote, probeObject);
            yield return null;

            bool localIsOwner = Networking.IsOwner(probeObject);

            UnityEngine.Object.DestroyImmediate(probeObject);

            bool passed = localIsOwner;
            LogResult(testName, passed, "localIsOwner=" + localIsOwner +
                " (expected true — SetOwner to a remote player should remain a no-op)");
        }

        [UnityTest]
        public IEnumerator TsStart_RealConstruction_CachesRealLocalPlayerIdentity()
        {
            const string testName = "TsStart_RealConstruction_CachesRealLocalPlayerIdentity";

            yield return StartClientSim();

            GameObject go = new GameObject("Process_TsStart");
            TsProcessTestSubclass process = go.AddComponent<TsProcessTestSubclass>();
            process.TsConstruct((TsRoot)null);

            string expectedId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            int expectedIntId = Networking.LocalPlayer.playerId;
            string cachedId = PrivateFieldAccess.GetField<string>(process, "_localPlayerId");
            int cachedIntId = PrivateFieldAccess.GetField<int>(process, "_localPlayerIdInt");

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = expectedId == cachedId && expectedIntId == cachedIntId;
            LogResult(testName, passed, "expectedId=" + expectedId + " cachedId=" + cachedId +
                " expectedIntId=" + expectedIntId + " cachedIntId=" + cachedIntId);
        }

        [UnityTest]
        public IEnumerator StartProcess_FreshInstance_RealSetProcessOwnerClaimsLocalOwnership()
        {
            const string testName = "StartProcess_FreshInstance_RealSetProcessOwnerClaimsLocalOwnership";

            yield return StartClientSim();

            GameObject go = new GameObject("Process_StartProcess");
            TsProcessTestSubclass process = go.AddComponent<TsProcessTestSubclass>();
            process.TsConstruct((TsRoot)null);
            int ownerIntIdBefore = PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt");

            process.StartProcess();
            yield return null;

            string expectedOwnerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            int expectedOwnerIntId = Networking.LocalPlayer.playerId;
            string ownerId = PrivateFieldAccess.GetField<string>(process, "_ownerId");
            int ownerIntId = PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt");
            bool isUnityOwner = Networking.IsOwner(go);
            int startedCount = process.OnProcessStartedCount;

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = ownerIntIdBefore == 0 && expectedOwnerId == ownerId &&
                expectedOwnerIntId == ownerIntId && isUnityOwner && startedCount == 1;
            LogResult(testName, passed, "ownerIntIdBefore=" + ownerIntIdBefore +
                " expectedOwnerId=" + expectedOwnerId + " ownerId=" + ownerId +
                " expectedOwnerIntId=" + expectedOwnerIntId + " ownerIntId=" + ownerIntId +
                " isUnityOwner=" + isUnityOwner + " startedCount=" + startedCount);
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RemoteNamedOwnerLeaves_LocalTakesOverProcess()
        {
            const string testName = "OnPlayerLeft_RemoteNamedOwnerLeaves_LocalTakesOverProcess";

            yield return StartClientSim();

            GameObject go = new GameObject("Process_OnPlayerLeft");
            TsProcessTestSubclass process = go.AddComponent<TsProcessTestSubclass>();
            process.TsConstruct((TsRoot)null);

            const string remoteName = "OwnerWhoLeaves";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");

            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_ownerId", TsPlayer.GetPlayerID(remote));
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", remote.playerId);

            // ClientSim's OnPlayerLeft broadcast only reaches compiled UdonBehaviour VM
            // instances registered with UdonManager. This test's process is a plain,
            // never-compiled UdonSharpBehaviour proxy with no backing Udon program, so
            // it's never in that registry and can't receive the event organically — it
            // needs direct invocation with a real VRCPlayerApi instead. ClientSim's
            // contribution here is the real player object and real player-list
            // removal, not dispatch.
            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            process.OnPlayerLeft(remote);

            int localId = Networking.LocalPlayer.playerId;
            int ownerIntIdAfter = PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt");
            int abandonedCount = process.OnOwnerAbandonedProcessCount;
            bool wasUnityOwnerAtLeaveTime = Networking.IsOwner(go);

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = localId == ownerIntIdAfter && abandonedCount == 1;
            LogResult(testName, passed, "localId=" + localId + " ownerIntIdAfter=" + ownerIntIdAfter +
                " abandonedCount=" + abandonedCount + " wasUnityOwnerAtLeaveTime=" + wasUnityOwnerAtLeaveTime);
        }

        [UnityTest]
        public IEnumerator OnOwnershipTransferred_NamedOwnerGone_LocalTakesOverProcess()
        {
            const string testName = "OnOwnershipTransferred_NamedOwnerGone_LocalTakesOverProcess";

            yield return StartClientSim();

            GameObject go = new GameObject("Process_OnOwnershipTransferred");
            TsProcessTestSubclass process = go.AddComponent<TsProcessTestSubclass>();
            process.TsConstruct((TsRoot)null);

            const string remoteName = "GoneOwner";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remotePlayerId = TsPlayer.GetPlayerID(remote);
            int remoteIntId = remote.playerId;

            // Remove the player BEFORE naming them as owner (opposite order from the
            // OnPlayerLeft test above) so ClientSim's real OnPlayerLeft dispatch sees
            // _ownerPlayerIdInt == 0 (no match) and does nothing — isolating this test
            // to OnOwnershipTransferred's own takeover logic, called directly since
            // ClientSim never actually fires it (ownership never really moves locally).
            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_ownerId", remotePlayerId);
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", remoteIntId);

            process.OnOwnershipTransferred(remote);

            int localId = Networking.LocalPlayer.playerId;
            int ownerIntIdAfter = PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt");
            int abandonedCount = process.OnOwnerAbandonedProcessCount;

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = localId == ownerIntIdAfter && abandonedCount == 1;
            LogResult(testName, passed, "localId=" + localId + " ownerIntIdAfter=" + ownerIntIdAfter +
                " abandonedCount=" + abandonedCount);
        }

        [UnityTest]
        public IEnumerator OnOwnershipTransferred_NamedOwnerSuspended_LocalTakesOverProcess()
        {
            // Covers the other half of `namedOwner == null || namedOwner.isSuspended`
            // — OnOwnershipTransferred_NamedOwnerGone_LocalTakesOverProcess above only
            // covers the null half.
            const string testName = "OnOwnershipTransferred_NamedOwnerSuspended_LocalTakesOverProcess";

            yield return StartClientSim();

            GameObject go = new GameObject("Process_OnOwnershipTransferred_Suspended");
            TsProcessTestSubclass process = go.AddComponent<TsProcessTestSubclass>();
            process.TsConstruct((TsRoot)null);

            const string remoteName = "SuspendedOwner";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");

            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_ownerId", TsPlayer.GetPlayerID(remote));
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", remote.playerId);

            // Mark suspended WITHOUT removing — still present/findable via
            // TsPlayer.FindPlayerByID, unlike the "gone" test above.
            // ClientSimPlayer.isSuspended is a plain public field; VRCPlayerApi.
            // isSuspended reads it through ClientSim's wired delegate
            // (ClientSimMain.cs: VRCPlayerApi._isSuspendedDelegate += _playerManager.
            // IsSuspended), so setting it here is a real, not synthetic, value read
            // back through the real VRCPlayerApi property.
            remote.GetClientSimPlayer().isSuspended = true;

            process.OnOwnershipTransferred(remote);

            int localId = Networking.LocalPlayer.playerId;
            int ownerIntIdAfter = PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt");
            int abandonedCount = process.OnOwnerAbandonedProcessCount;
            bool remoteReportsSuspended = remote.isSuspended;

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = localId == ownerIntIdAfter && abandonedCount == 1 && remoteReportsSuspended;
            LogResult(testName, passed, "localId=" + localId + " ownerIntIdAfter=" + ownerIntIdAfter +
                " abandonedCount=" + abandonedCount + " remoteReportsSuspended=" + remoteReportsSuspended);
        }

        [UnityTest]
        public IEnumerator OnDeserialization_NamedOwnerGone_ReassertsLocalOwnership()
        {
            const string testName = "OnDeserialization_NamedOwnerGone_ReassertsLocalOwnership";

            yield return StartClientSim();

            GameObject go = new GameObject("Process_OnDeserialization");
            TsProcessTestSubclass process = go.AddComponent<TsProcessTestSubclass>();
            process.TsConstruct((TsRoot)null);

            const string remoteName = "StaleOwner";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remotePlayerId = TsPlayer.GetPlayerID(remote);
            int remoteIntId = remote.playerId;

            // Same ordering as the OnOwnershipTransferred test: remove first, then
            // name them as owner, so this isolates OnDeserialization's own recovery
            // logic without OnPlayerLeft's takeover firing first.
            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_ownerId", remotePlayerId);
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", remoteIntId);

            // Networking.IsOwner(gameObject) is always true locally, so
            // OnDeserialization's recovery block — which requires exactly that — is
            // reachable through a genuinely organic call, unlike OnOwnershipTransferred.
            process.OnDeserialization();

            int localId = Networking.LocalPlayer.playerId;
            int ownerIntIdAfter = PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt");

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = localId == ownerIntIdAfter;
            LogResult(testName, passed, "localId=" + localId + " ownerIntIdAfter=" + ownerIntIdAfter);
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_PlayerNotSuspended_IsNoOp()
        {
            const string testName = "OnPlayerSuspendChanged_PlayerNotSuspended_IsNoOp";

            yield return StartClientSim();

            GameObject go = new GameObject("Process_OnPlayerSuspendChanged");
            TsProcessTestSubclass process = go.AddComponent<TsProcessTestSubclass>();
            process.TsConstruct((TsRoot)null);

            const string remoteName = "NotSuspended";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");

            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_ownerId", TsPlayer.GetPlayerID(remote));
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", remote.playerId);

            // player.isSuspended is false by default here, so the method's first guard
            // (`if (!player.isSuspended || ...) return;`) must reject this immediately.
            // This is the only branch of OnPlayerSuspendChanged reachable in this
            // environment: its one meaningful line — Networking.SetOwner(Networking.
            // LocalPlayer, gameObject) — sits behind `if (Networking.IsOwner(gameObject))
            // return;`, and Networking.IsOwner(gameObject) is unconditionally true for
            // the local player under ClientSim for any real GameObject, with no way to
            // construct otherwise. Verifying that branch needs real multi-client Build
            // & Test.
            process.OnPlayerSuspendChanged(remote);

            int ownerIntIdAfter = PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt");
            int expectedUnchanged = remote.playerId;

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = ownerIntIdAfter == expectedUnchanged;
            LogResult(testName, passed, "ownerIntIdAfter=" + ownerIntIdAfter +
                " expectedUnchanged=" + expectedUnchanged);
        }
    }
}
