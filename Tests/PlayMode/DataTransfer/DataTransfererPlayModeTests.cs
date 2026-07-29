using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.DataTransfer
{
    // DataChunkReceiver.OnTransferStarted()'s owner path touches Networking.LocalPlayer, which
    // requires a real ClientSim environment, so TransferData() is driven through here as the
    // owner rather than via direct hook invocation.
    //
    // SendCustomEventDelayedSeconds is an empty method body on the plain C# UdonSharpBehaviour
    // base class, so every deferred step this chain schedules with it (_StartNextReadyCheck,
    // _EmitDataReceptionCompleted, _EmitDataReceptionStopped) is invoked directly below rather
    // than waited on.
    public class DataTransfererPlayModeTests : ProcessPlayModeTestBase
    {
        private static string PlayerId(VRCPlayerApi player) => player.displayName + "#" + player.playerId;

        private static string BuildString(int length, char c = 'x') => new string(c, length);

        private static string[] GetTrackedPlayerIds(object transferer)
        {
            return (string[])PrivateFieldAccess.InvokeInstance(transferer, "GetTrackedPlayerIds");
        }

        [UnityTest]
        public IEnumerator TransferData_RealOwnerSingleChunkMessage_FullCascadeToOnTransferCompletedEvent()
        {
            yield return StartClientSim();
            string localId = PlayerId(Networking.LocalPlayer);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            transferer.SubscribeToAllTransferEvents();

            transferer.TransferData("hello real world", new[] { localId });

            Assert.AreEqual(1, transferer.OnTransferStartedEventCount);
            Assert.AreEqual(1, transferer.OnTransferChunkEventCount,
                "The owner is the sole target, so the single chunk must be sent, self-received, and acked synchronously.");
            Assert.AreEqual(1, transferer.LastChunkIndex);
            Assert.AreEqual(1, transferer.LastTotalChunks);
            Assert.AreEqual(0, transferer.OnTransferCompletedEventCount,
                "Completion is deferred - must not fire until the emit actually runs.");

            transferer._EmitDataReceptionCompleted();

            Assert.AreEqual(1, transferer.OnTransferCompletedEventCount);
            Assert.AreEqual("hello real world", transferer.LastData);
        }

        [UnityTest]
        public IEnumerator TransferData_RealOwnerMultiChunkMessage_AdvancesThroughEachChunkToCompletion()
        {
            yield return StartClientSim();
            string localId = PlayerId(Networking.LocalPlayer);
            // DataChunker.CHUNK_SIZE is 2500; 6001 chars spans exactly 3 chunks (2500, 2500, 1001).
            string data = BuildString(6001);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            transferer.SubscribeToAllTransferEvents();

            transferer.TransferData(data, new[] { localId });

            Assert.AreEqual(1, transferer.OnTransferChunkEventCount);
            Assert.AreEqual(1, transferer.LastChunkIndex);
            Assert.AreEqual(3, transferer.LastTotalChunks);

            transferer._StartNextReadyCheck();

            Assert.AreEqual(2, transferer.OnTransferChunkEventCount);
            Assert.AreEqual(2, transferer.LastChunkIndex);

            transferer._StartNextReadyCheck();

            Assert.AreEqual(3, transferer.OnTransferChunkEventCount);
            Assert.AreEqual(3, transferer.LastChunkIndex);
            Assert.AreEqual(0, transferer.OnTransferCompletedEventCount,
                "The last chunk's completion event is still deferred at this point.");

            transferer._EmitDataReceptionCompleted();

            Assert.AreEqual(1, transferer.OnTransferCompletedEventCount);
            Assert.AreEqual(data, transferer.LastData,
                "The reassembled message across all three real chunks must match the original data exactly.");
        }

        [UnityTest]
        public IEnumerator StartNextReadyCheck_RealTargetLeavesDuringInterChunkGap_ExcludedFromNextChunk()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Staying");
            Players.SpawnRemotePlayer("Leaving");
            yield return null;
            yield return null;
            VRCPlayerApi staying = ClientSimPlayerEnvironment.FindPlayerByName("Staying");
            VRCPlayerApi leaving = ClientSimPlayerEnvironment.FindPlayerByName("Leaving");
            Assert.IsNotNull(staying, "Setup sanity check: Staying was not found.");
            Assert.IsNotNull(leaving, "Setup sanity check: Leaving was not found.");
            string stayingId = PlayerId(staying);
            string leavingId = PlayerId(leaving);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            // 3000 chars spans exactly 2 chunks (2500, 500), leaving a real inter-chunk gap.
            transferer.TransferData(BuildString(3000), new[] { stayingId, leavingId });

            // Neither remote target is the local player, so nothing auto-acks; mark both
            // targets' chunks received directly instead.
            transferer.BroadcastAddReadyPlayer(stayingId);
            transferer.BroadcastAddReadyPlayer(leavingId);

            Players.RemovePlayer(leaving);
            transferer._StartNextReadyCheck();

            string[] trackedIds = GetTrackedPlayerIds(transferer);
            Assert.IsTrue(System.Array.IndexOf(trackedIds, stayingId) >= 0,
                "The still-active real target must carry into the next chunk's ready check.");
            Assert.IsFalse(System.Array.IndexOf(trackedIds, leavingId) >= 0,
                "The real GetAllPlayers() scan must exclude the departed target from the next chunk.");
        }

        [UnityTest]
        public IEnumerator StartNextReadyCheck_RealAllTargetsLeaveDuringInterChunkGap_BroadcastsStoppedInstead()
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
            string firstId = PlayerId(first);
            string secondId = PlayerId(second);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            transferer.SubscribeToAllTransferEvents();
            transferer.TransferData(BuildString(3000), new[] { firstId, secondId });
            transferer.BroadcastAddReadyPlayer(firstId);
            transferer.BroadcastAddReadyPlayer(secondId);

            Players.RemovePlayer(first);
            Players.RemovePlayer(second);
            transferer._StartNextReadyCheck();

            Assert.AreEqual(0, transferer.OnTransferCompletedEventCount);
            transferer._EmitDataReceptionStopped();
            Assert.AreEqual(1, transferer.OnTransferStoppedEventCount,
                "When every real target departs during the inter-chunk gap, the transfer must stop instead of stalling forever.");
        }

        [UnityTest]
        public IEnumerator OnProcessUpdate_RealAllTargetsLeaveDuringActiveFirstChunk_CancelsStalledTransferInstead()
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
            string firstId = PlayerId(first);
            string secondId = PlayerId(second);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            transferer.SubscribeToAllTransferEvents();
            // Neither target ever acks, so the first chunk's ready check is genuinely still
            // active (not the inter-chunk gap - _isRunning stays true throughout this test).
            transferer.TransferData("stalled by two real departures", new[] { firstId, secondId });

            // Real OnPlayerLeft, invoked directly (ClientSim never organically dispatches VRC
            // lifecycle callbacks to a plain UdonSharpBehaviour), removes each departed target
            // from the tracked set one at a time - exactly like a genuine mid-instance departure.
            Players.RemovePlayer(first);
            transferer.OnPlayerLeft(first);
            Players.RemovePlayer(second);
            transferer.OnPlayerLeft(second);

            Assert.AreEqual(0, GetTrackedPlayerIds(transferer).Length,
                "Setup sanity check: both real targets must be gone from the tracked set.");
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(transferer, "_isRunning"),
                "Setup sanity check: CheckAllPlayersReady's own empty-list guard must leave the ready " +
                "check genuinely stuck running with nobody left to wait for - this is the exact stall " +
                "ChunkedTransferSession.OnProcessUpdate's own comment says its real periodic tick exists to detect and recover from.");

            PrivateFieldAccess.InvokeInstance(transferer, "OnProcessUpdate");

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(transferer, "_isRunning"),
                "OnProcessUpdate must detect the fully-abandoned ready check and cancel the stalled transfer.");

            transferer._EmitDataReceptionStopped();
            Assert.AreEqual(1, transferer.OnTransferStoppedEventCount,
                "The self-healing cancel must still broadcast the stopped event, exactly like an explicit CancelDataTransfer() call.");
        }

        [UnityTest]
        public IEnumerator OnOwnerAbandonedProcess_RealNewOwnerMidTransfer_StopsReadyCheckForRealTakeover()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("PendingTarget");
            yield return null;
            yield return null;
            VRCPlayerApi pendingTarget = ClientSimPlayerEnvironment.FindPlayerByName("PendingTarget");
            Assert.IsNotNull(pendingTarget, "Setup sanity check: PendingTarget was not found.");
            string pendingTargetId = PlayerId(pendingTarget);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            transferer.SubscribeToAllTransferEvents();
            // The sole target never acks, so the ready check is genuinely still pending below.
            transferer.TransferData("mid transfer data", new[] { pendingTargetId });

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(transferer, "_isRunning"),
                "Setup sanity check: the ready check must still be pending (the target never acked).");

            PrivateFieldAccess.InvokeInstance(transferer, "OnOwnerAbandonedProcess");

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(transferer, "_isRunning"),
                "A real new owner taking over mid-transfer must stop the stalled ready check, not leave a zombie process running.");

            transferer._EmitDataReceptionStopped();
            Assert.AreEqual(1, transferer.OnTransferStoppedEventCount);
        }

        [UnityTest]
        public IEnumerator OnOwnerAbandonedProcess_RealNewOwnerDuringInterChunkGap_StopsThePendingTransfer()
        {
            yield return StartClientSim();
            string localId = PlayerId(Networking.LocalPlayer);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            transferer.SubscribeToAllTransferEvents();
            // The owner is the sole target, so chunk 1 of this 3-chunk (6001-char) message
            // completes synchronously, landing the process in the real inter-chunk gap:
            // _isRunning is false and _pendingNextChunk is true.
            transferer.TransferData(BuildString(6001), new[] { localId });

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(transferer, "_isRunning"),
                "Setup sanity check: chunk 1 must have already completed, landing in the inter-chunk gap.");
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(transferer, "_pendingNextChunk"),
                "Setup sanity check: the deferred _StartNextReadyCheck call must be genuinely pending.");

            PrivateFieldAccess.InvokeInstance(transferer, "OnOwnerAbandonedProcess");

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(transferer, "_pendingNextChunk"),
                "A real new owner taking over during the inter-chunk gap must not let the old owner's " +
                "deferred next-chunk continuation resume - it must reset the pending transfer instead.");
            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(transferer, "_currentChunkIndex"),
                "ResetInternalTransferData must fully clear the chunk index, not just the pending flag.");

            transferer._EmitDataReceptionStopped();
            Assert.AreEqual(1, transferer.OnTransferStoppedEventCount,
                "The abandoned gap-state transfer must still broadcast the stopped event for real receivers to clean up.");
        }

        [UnityTest]
        public IEnumerator CancelDataTransfer_RealActiveTransfer_StopsAndBroadcastsStoppedEvent()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Target");
            yield return null;
            yield return null;
            VRCPlayerApi target = ClientSimPlayerEnvironment.FindPlayerByName("Target");
            Assert.IsNotNull(target, "Setup sanity check: Target was not found.");
            string targetId = PlayerId(target);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            transferer.SubscribeToAllTransferEvents();
            transferer.TransferData("cancel me for real", new[] { targetId });

            transferer.CancelDataTransfer();

            Assert.AreEqual(0, transferer.OnTransferCompletedEventCount);
            transferer._EmitDataReceptionStopped();
            Assert.AreEqual(1, transferer.OnTransferStoppedEventCount,
                "A real cancel mid-transfer must broadcast the stopped event once its deferred emit runs.");
        }

        [UnityTest]
        public IEnumerator CancelDataTransfer_RealDuringInterChunkGap_StopsThePendingTransfer()
        {
            yield return StartClientSim();
            string localId = PlayerId(Networking.LocalPlayer);

            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            transferer.SubscribeToAllTransferEvents();
            // The owner is the sole target, so chunk 1 completes synchronously, landing the
            // process in the real _pendingNextChunk=true / _isRunning=false inter-chunk gap.
            transferer.TransferData(BuildString(6001), new[] { localId });

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(transferer, "_pendingNextChunk"),
                "Setup sanity check: the deferred _StartNextReadyCheck call must be genuinely pending.");

            transferer.CancelDataTransfer();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(transferer, "_pendingNextChunk"),
                "A real cancel during the inter-chunk gap must clear the pending next-chunk continuation.");

            transferer._EmitDataReceptionStopped();
            Assert.AreEqual(1, transferer.OnTransferStoppedEventCount,
                "A real gap-state cancel must still broadcast the stopped event once its deferred emit runs.");
        }
    }
}
