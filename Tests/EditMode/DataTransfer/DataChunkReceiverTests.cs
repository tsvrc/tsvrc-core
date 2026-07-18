using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers DataChunkReceiver: the security-critical BroadcastDataChunkReceived validation
    // chain, receiver-state reset, and message reassembly.
    //
    // DataChunkReceiver.OnTransferStarted() unconditionally reads Networking.LocalPlayer.playerId
    // when IsProcessOwner() is true, and Networking.LocalPlayer is null in Edit Mode. This means
    // TransferData()/StartReadyCheck() as the owner can never complete in Edit Mode once
    // DataChunkReceiver is in the inheritance chain - every test below instead seeds the
    // "mid-reception" state directly via reflection (_transferActive, _expectedSenderPlayerId,
    // tracked/running state) rather than driving it through the real start flow. The real
    // OnTransferStarted() owner-branch behavior itself needs Play Mode.
    //
    // _isSendingChunk is set directly via reflection for tests needing fine-grained control over
    // the chunk arguments (mirroring the _isBroadcasting bypass technique used throughout this
    // suite), bypassing the Edit-Mode limitation that NetworkCalling.CallingPlayer is always null
    // outside a real dispatch.
    public class DataChunkReceiverTests : DataTransferTestBase
    {
        private static string[] Ids(params string[] ids) => ids;

        // Seeds enough state to call BroadcastDataChunkReceived directly, without ever going
        // through TransferData()'s owner-side start flow.
        private static void SeedActiveTransfer(DataChunkReceiverTestSubclass receiver, int expectedSenderPlayerId)
        {
            SetTransferActive(receiver, true);
            SetExpectedSenderPlayerId(receiver, expectedSenderPlayerId);
        }

        // Additionally seeds a running ready check with the given tracked players, for tests
        // that need NotifyChunkReceived()'s SetReady() ACK to actually take effect.
        private static void SeedActiveTransferWithReadyCheck(DataChunkReceiverTestSubclass receiver, string[] trackedIds, int expectedSenderPlayerId)
        {
            PrivateFieldAccess.SetField(receiver, "_isRunning", true);
            SetTrackedPlayerIds(receiver, trackedIds);
            SetReadyCheckActive(receiver, true);
            SeedActiveTransfer(receiver, expectedSenderPlayerId);
        }

        [Test]
        public void BroadcastDataChunkReceived_TransferNotActive_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            // _transferActive left at its default false.

            receiver.BroadcastDataChunkReceived("chunk", 1, 1, Ids("TestOwner#" + OwnerPlayerId));

            CollectionAssert.AreEqual(new string[0], receiver.PeekReceivedChunks());
            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_DirectCallWithoutSendingFlag_RejectedByCallerGuard()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            SeedActiveTransfer(receiver, OwnerPlayerId);
            // _isSendingChunk left false: NetworkCalling.CallingPlayer is null outside real
            // dispatch, so the caller guard rejects this regardless of _expectedSenderPlayerId.

            receiver.BroadcastDataChunkReceived("chunk", 1, 1, Ids("TestOwner#" + OwnerPlayerId));

            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_NullPlayerIds_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            Assert.DoesNotThrow(() => receiver.BroadcastDataChunkReceived("chunk", 1, 1, null));

            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_LocalPlayerNotInPlayerIds_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            receiver.BroadcastDataChunkReceived("chunk", 1, 1, Ids("SomeoneElse#9"));

            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_NullDataChunk_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            receiver.BroadcastDataChunkReceived(null, 1, 1, Ids(ownerId));

            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_TotalChunksZero_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            receiver.BroadcastDataChunkReceived("chunk", 1, 0, Ids(ownerId));

            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_TotalChunksExceedsMax_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            // _maxChunks = ceil(500000 / 2500) = 200.
            receiver.BroadcastDataChunkReceived("chunk", 1, 201, Ids(ownerId));

            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_TotalChunksExactlyMax_Accepted()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            receiver.BroadcastDataChunkReceived("chunk", 1, 200, Ids(ownerId));

            Assert.AreEqual(1, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_ChunkIndexZero_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            receiver.BroadcastDataChunkReceived("chunk", 0, 2, Ids(ownerId));

            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_ChunkIndexExceedsTotalChunks_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            receiver.BroadcastDataChunkReceived("chunk", 3, 2, Ids(ownerId));

            Assert.AreEqual(0, receiver.OnChunkStoredCount);
        }

        [Test]
        public void BroadcastDataChunkReceived_Valid_StoresChunkFiresHookAndAcksViaSetReady()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransferWithReadyCheck(receiver, new[] { ownerId, "Other#1" }, OwnerPlayerId); // 2 tracked, stays running
            SetIsSendingChunk(receiver, true);

            receiver.BroadcastDataChunkReceived("hello-chunk", 1, 1, Ids(ownerId, "Other#1"));

            Assert.AreEqual(1, receiver.OnChunkStoredCount);
            CollectionAssert.AreEqual(new[] { 1 }, receiver.OnChunkStoredChunkIndexes);
            CollectionAssert.AreEqual(new[] { "hello-chunk" }, receiver.PeekReceivedChunks());
            Assert.IsTrue(InvokeIsPlayerReady(receiver, ownerId), "NotifyChunkReceived must ACK via SetReady().");
        }

        [Test]
        public void BroadcastDataChunkReceived_TotalChunksMismatchOnSecondChunk_Rejected()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);
            receiver.BroadcastDataChunkReceived("chunk1", 1, 2, Ids(ownerId)); // locks in totalChunks=2

            receiver.BroadcastDataChunkReceived("chunk2", 2, 3, Ids(ownerId)); // mismatched totalChunks

            Assert.AreEqual(1, receiver.OnChunkStoredCount, "The mismatched second call must be rejected.");
        }

        [Test]
        public void BroadcastDataChunkReceived_DuplicateChunkIndex_SecondDeliveryIgnored()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);
            receiver.BroadcastDataChunkReceived("first", 1, 1, Ids(ownerId));

            receiver.BroadcastDataChunkReceived("second-attempt", 1, 1, Ids(ownerId));

            Assert.AreEqual(1, receiver.OnChunkStoredCount, "A duplicate delivery for the same index must not re-fire OnChunkStored.");
            CollectionAssert.AreEqual(new[] { "first" }, receiver.PeekReceivedChunks());
        }

        [Test]
        public void BroadcastDataChunkReceived_OutOfOrderArrival_StoredAtCorrectIndexRegardlessOfArrivalOrder()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedAsOwner(receiver);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetIsSendingChunk(receiver, true);

            receiver.BroadcastDataChunkReceived("second", 2, 2, Ids(ownerId)); // chunk 2 arrives first
            receiver.BroadcastDataChunkReceived("first", 1, 2, Ids(ownerId));

            CollectionAssert.AreEqual(new[] { "first", "second" }, receiver.PeekReceivedChunks(),
                "Chunks are placed by chunkIndex, not arrival order.");
        }

        [Test]
        public void OnTransferCompleted_ReassemblesAllChunksInOrderAndClearsReceivedChunks()
        {
            // Invoked directly (bypassing the whole TransferData/StartProcess chain, which
            // would crash - see the file header) to isolate this test to OnTransferCompleted's
            // own reassembly logic.
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SetReceivedChunks(receiver, new[] { "hello", "world" });

            PrivateFieldAccess.InvokeInstance(receiver, "OnTransferCompleted");

            Assert.AreEqual(1, receiver.OnChunksAssembledCount);
            Assert.AreEqual("helloworld", receiver.LastAssembledDataSeen);
            CollectionAssert.AreEqual(new string[0], receiver.PeekReceivedChunks());
        }

        [Test]
        public void OnTransferCompleted_PartiallyPopulatedReceivedChunks_DoesNotThrowAndAssemblesEmptyString()
        {
            // NotifyTrackedPlayersDataTransferCompleted targets NetworkEventTarget.All, so
            // OnTransferCompleted fires on every player physically in the instance - including
            // one tracked for only a prefix of the transfer (e.g. RemoveTrackedPlayers called
            // mid-transfer, or filtered out by the inter-chunk-gap departure handling while still
            // present). That player's _receivedChunks keeps its full-size allocation from the
            // chunks they DID receive, with null gaps for the chunks sent after their removal
            // (silently dropped by BroadcastDataChunkReceived's own playerIds check).
            // ReassembleMessage requires every element non-null, so OnTransferCompleted must
            // detect this case and report an empty result instead of reassembling.
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SetReceivedChunks(receiver, new[] { "first", null, null });

            Assert.DoesNotThrow(() => PrivateFieldAccess.InvokeInstance(receiver, "OnTransferCompleted"));

            Assert.AreEqual(1, receiver.OnChunksAssembledCount);
            Assert.AreEqual("", receiver.LastAssembledDataSeen);
            CollectionAssert.AreEqual(new string[0], receiver.PeekReceivedChunks());
        }

        [Test]
        public void OnTransferCompleted_NeverTrackedBystander_AssemblesEmptyStringUnchanged()
        {
            // A player who was never tracked at all has an empty _receivedChunks (never
            // allocated by ResetReceiverState), which the HasAllChunks check in OnTransferCompleted
            // must still treat as complete rather than incomplete.
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            // _receivedChunks left at its default empty array.

            PrivateFieldAccess.InvokeInstance(receiver, "OnTransferCompleted");

            Assert.AreEqual(1, receiver.OnChunksAssembledCount);
            Assert.AreEqual("", receiver.LastAssembledDataSeen);
        }

        [Test]
        public void OnTransferStopped_ResetsReceiverState()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetExpectedTotalChunks(receiver, 3);
            SetReceivedChunks(receiver, new[] { "a", null, null });

            PrivateFieldAccess.InvokeInstance(receiver, "OnTransferStopped");

            Assert.IsFalse(GetTransferActive(receiver));
            CollectionAssert.AreEqual(new string[0], receiver.PeekReceivedChunks());
            Assert.AreEqual(0, GetExpectedSenderPlayerId(receiver));
            Assert.AreEqual(0, GetExpectedTotalChunks(receiver));
        }

        [Test]
        public void ResetReceiverState_ClearsEveryField()
        {
            var receiver = CreateProcess<DataChunkReceiverTestSubclass>();
            SeedActiveTransfer(receiver, OwnerPlayerId);
            SetExpectedTotalChunks(receiver, 5);
            SetReceivedChunks(receiver, new[] { "a", "b" });

            PrivateFieldAccess.InvokeInstance(receiver, "ResetReceiverState");

            Assert.IsFalse(GetTransferActive(receiver));
            Assert.AreEqual(0, GetExpectedSenderPlayerId(receiver));
            Assert.AreEqual(0, GetExpectedTotalChunks(receiver));
            CollectionAssert.AreEqual(new string[0], receiver.PeekReceivedChunks());
        }
    }
}
