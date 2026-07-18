using System.Collections;
using NUnit.Framework;
using Tsvrc.Core.Generated;
using Tsvrc.Player;
using Tsvrc.Tests.EditMode;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // Real multi-player/multi-chunk Play Mode tests for the DataTransferer chain, targeting
    // exactly what Edit Mode could not reach at all:
    //   - DataChunkReceiver.OnTransferStarted() unconditionally reads Networking.LocalPlayer
    //     when IsProcessOwner() is true, which is null in Edit Mode - so TransferData() as the
    //     owner could never even be called there. Every test here drives it for real.
    //   - ChunkedTransferSession._StartNextReadyCheck() unconditionally calls
    //     TsPlayer.GetAllPlayers(), which returns an empty array in Edit Mode - so multi-chunk
    //     progression could only ever be shown to "stop" there, never to actually continue.
    //     Real ClientSim players let this suite prove the real continuation path.
    //
    // Every deferred emit (_EmitDataReceptionStopped/_EmitDataReceptionCompleted) is still
    // manually invoked: SendCustomEventDelayedSeconds is a no-op stub in the Editor proxy in
    // both Edit Mode and Play Mode alike.
    //
    // Follows the exact harness/verification pattern established by
    // Tests/PlayMode/Tracking/ReadyCheckProcess/ReadyCheckProcessPlayModeTests.cs: NUnit Assert
    // failures are silent in this project's Play Mode environment, so every test computes its
    // own bool and logs exactly one PLAYMODE_TEST_RESULT marker.
    public class DataTransfererPlayModeTests
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

        [UnityTest]
        public IEnumerator TransferData_RealLocalOwner_SingleChunkSinglePlayer_CompletesEndToEnd()
        {
            const string testName = "TransferData_RealLocalOwner_SingleChunkSinglePlayer_CompletesEndToEnd";

            yield return StartClientSim();

            GameObject go = new GameObject("DataTransferer_RealOwnerSingleChunk");
            DataTransfererTestSubclass transferer = go.AddComponent<DataTransfererTestSubclass>();
            transferer.TsConstruct((TsRoot)null);
            transferer.SubscribeToAllTransferEvents();

            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            transferer.TransferData("hello world", new[] { localId });
            yield return null;
            transferer._EmitDataReceptionCompleted(); // deferred emit, manually fired

            string lastData = transferer.LastData;
            int completedCount = transferer.OnTransferCompletedEventCount;
            bool stillRunning = transferer.IsProcessRunning();

            Object.DestroyImmediate(go);

            bool passed = lastData == "hello world" && completedCount == 1 && !stillRunning;
            LogResult(testName, passed, "lastData=" + lastData + " completedCount=" + completedCount + " stillRunning=" + stillRunning);
        }

        [UnityTest]
        public IEnumerator TransferData_RealLocalOwner_MultiChunkMessage_ProgressesThroughAllChunksAutomatically()
        {
            const string testName = "TransferData_RealLocalOwner_MultiChunkMessage_ProgressesThroughAllChunksAutomatically";

            yield return StartClientSim();

            GameObject go = new GameObject("DataTransferer_RealOwnerMultiChunk");
            DataTransfererTestSubclass transferer = go.AddComponent<DataTransfererTestSubclass>();
            transferer.TsConstruct((TsRoot)null);
            transferer.SubscribeToAllTransferEvents();

            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            string data = new string('x', 2500 * 2 + 10); // 3 chunks

            transferer.TransferData(data, new[] { localId });
            yield return null;
            transferer._StartNextReadyCheck(); // real GetAllPlayers() -> chunk 2
            yield return null;
            transferer._StartNextReadyCheck(); // -> chunk 3 (last)
            yield return null;
            transferer._EmitDataReceptionCompleted();

            string lastData = transferer.LastData;
            int completedCount = transferer.OnTransferCompletedEventCount;
            int chunkEventCount = transferer.OnTransferChunkEventCount;

            Object.DestroyImmediate(go);

            bool passed = lastData == data && completedCount == 1 && chunkEventCount == 3;
            LogResult(testName, passed, "lastDataMatches=" + (lastData == data) +
                " completedCount=" + completedCount + " chunkEventCount=" + chunkEventCount);
        }

        [UnityTest]
        public IEnumerator TransferData_RealOwnerAndRealRemoteTracked_StaysRunningUntilRemoteAcks()
        {
            const string testName = "TransferData_RealOwnerAndRealRemoteTracked_StaysRunningUntilRemoteAcks";

            yield return StartClientSim();

            GameObject go = new GameObject("DataTransferer_RealMultiPlayer");
            DataTransfererTestSubclass transferer = go.AddComponent<DataTransfererTestSubclass>();
            transferer.TsConstruct((TsRoot)null);
            transferer.SubscribeToAllTransferEvents();

            ClientSimMain.SpawnRemotePlayer("RemoteReceiver");
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName("RemoteReceiver");
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            transferer.TransferData("hello", new[] { localId, remoteId }); // owner self-acks; remote doesn't
            yield return null;
            bool stillRunningAfterOwnAck = transferer.IsProcessRunning();

            // Simulates the remote's own ACK arriving at the owner (their own client would send
            // this over the network; here it's applied directly, the same technique
            // ReadyCheckProcess's own test suite uses for simulating a remote player's vote).
            transferer.BroadcastAddReadyPlayer(remoteId);
            bool completedAfterRemoteAck = !transferer.IsProcessRunning();
            int completedCountBeforeEmit = transferer.OnTransferCompletedEventCount;
            transferer._EmitDataReceptionCompleted();
            int completedCountAfterEmit = transferer.OnTransferCompletedEventCount;

            Object.DestroyImmediate(go);

            bool passed = stillRunningAfterOwnAck && completedAfterRemoteAck &&
                completedCountBeforeEmit == 0 && completedCountAfterEmit == 1;
            LogResult(testName, passed, "stillRunningAfterOwnAck=" + stillRunningAfterOwnAck +
                " completedAfterRemoteAck=" + completedAfterRemoteAck +
                " completedCountBeforeEmit=" + completedCountBeforeEmit +
                " completedCountAfterEmit=" + completedCountAfterEmit);
        }

        [UnityTest]
        public IEnumerator RemoteTrackedPlayerLeaves_DuringInterChunkGap_FilteredOutAndTransferContinuesWithOwnerAlone()
        {
            const string testName = "RemoteTrackedPlayerLeaves_DuringInterChunkGap_FilteredOutAndTransferContinuesWithOwnerAlone";

            yield return StartClientSim();

            GameObject go = new GameObject("DataTransferer_RemoteDepartsDuringGap");
            DataTransfererTestSubclass transferer = go.AddComponent<DataTransfererTestSubclass>();
            transferer.TsConstruct((TsRoot)null);
            transferer.SubscribeToAllTransferEvents();

            ClientSimMain.SpawnRemotePlayer("WillDepart");
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName("WillDepart");
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            string data = new string('x', 2500 * 2); // 2 chunks
            transferer.TransferData(data, new[] { localId, remoteId });
            yield return null;
            transferer.BroadcastAddReadyPlayer(remoteId); // remote also acks chunk 1 -> completes -> gap

            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            transferer._StartNextReadyCheck(); // real GetAllPlayers() now excludes the departed remote
            yield return null;
            // Chunk 2 auto-acked by the owner alone (sole remaining target).
            transferer._EmitDataReceptionCompleted();

            string lastData = transferer.LastData;
            int completedCount = transferer.OnTransferCompletedEventCount;

            Object.DestroyImmediate(go);

            bool passed = lastData == data && completedCount == 1;
            LogResult(testName, passed, "lastDataMatches=" + (lastData == data) + " completedCount=" + completedCount);
        }

        [UnityTest]
        public IEnumerator OnOwnerAbandonedProcess_RealTakeoverDuringActiveChunk_StopsTheReadyCheck()
        {
            const string testName = "OnOwnerAbandonedProcess_RealTakeoverDuringActiveChunk_StopsTheReadyCheck";

            yield return StartClientSim();

            GameObject go = new GameObject("DataTransferer_AbandonmentDuringChunk");
            DataTransfererTestSubclass transferer = go.AddComponent<DataTransfererTestSubclass>();
            transferer.TsConstruct((TsRoot)null);
            transferer.SubscribeToAllTransferEvents();

            ClientSimMain.SpawnRemotePlayer("OwnerWhoLeaves");
            yield return null;
            yield return null;
            VRCPlayerApi remoteOwner = FindPlayerByName("OwnerWhoLeaves");
            Assert.IsNotNull(remoteOwner, "Remote owner was not spawned.");
            string remoteOwnerId = TsPlayer.GetPlayerID(remoteOwner);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            // ClientSim has only one real Udon execution context, so the "remote owner" can't
            // actually run TransferData() itself - seed the mid-transfer state directly (same
            // technique PlayerTracker's own abandonment Play Mode tests use), then let the real
            // OnOwnerAbandonedProcess (real GetAllPlayers()-backed scan underneath it) run for real.
            PrivateFieldAccess.SetField(transferer, "_localPlayerId", localId);
            PrivateFieldAccess.SetField(transferer, "_localPlayerIdInt", Networking.LocalPlayer.playerId);
            PrivateFieldAccess.SetField(transferer, "_ownerId", remoteOwnerId);
            PrivateFieldAccess.SetField(transferer, "_ownerPlayerIdInt", remoteOwner.playerId);
            PrivateFieldAccess.SetField(transferer, "_isRunning", true);
            PrivateFieldAccess.SetField(transferer, "_trackedPlayerIds", new[] { remoteOwnerId, localId });
            PrivateFieldAccess.SetField(transferer, "_readyCheckActive", true);

            PrivateFieldAccess.InvokeInstance(transferer, "OnOwnerAbandonedProcess");

            bool stillRunning = transferer.IsProcessRunning();
            int stoppedCountBeforeEmit = transferer.OnTransferStoppedEventCount;
            transferer._EmitDataReceptionStopped();
            int stoppedCountAfterEmit = transferer.OnTransferStoppedEventCount;

            Object.DestroyImmediate(go);

            bool passed = !stillRunning && stoppedCountBeforeEmit == 0 && stoppedCountAfterEmit == 1;
            LogResult(testName, passed, "stillRunning=" + stillRunning +
                " stoppedCountBeforeEmit=" + stoppedCountBeforeEmit + " stoppedCountAfterEmit=" + stoppedCountAfterEmit);
        }
    }
}
