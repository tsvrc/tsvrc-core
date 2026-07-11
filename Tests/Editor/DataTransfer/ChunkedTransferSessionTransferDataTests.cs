using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // Covers TransferData's own guards and its initial chunk-splitting/sending, tested through
    // ChunkedTransferSessionTestSubclass directly (not layered with DataSender/DataChunkReceiver)
    // so OnDataChunkSendRequested stays a recorded no-op hook rather than actually broadcasting -
    // this isolates ChunkedTransferSession's own state machine from the broadcast/receive chain
    // built on top of it, tested separately in DataChunkReceiver/DataSender/DataTransferer files.
    public class ChunkedTransferSessionTransferDataTests : DataTransferTestBase
    {
        [Test]
        public void TransferData_AlreadyRunning_WarnsAndDoesNotRestart()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            // Two tracked players so the ready check stays running (owner's chunk delivery
            // isn't simulated at this layer, so nothing auto-acks).
            session.TransferData("hello", new[] { ownerId, "Other#1" });
            Assert.AreEqual(1, session.OnChunkSequenceStartedCount);

            LogAssert.Expect(LogType.Warning, "[TsvrcDataSender] Transfer already in progress. Call CancelDataTransfer() first.");
            session.TransferData("world", new[] { ownerId });

            Assert.AreEqual(1, session.OnChunkSequenceStartedCount, "A second call while running must not restart the sequence.");
            CollectionAssert.AreEqual(new[] { "hello" }, session.OnDataChunkSendRequestedDataChunks,
                "The stray call's payload must never be split/sent.");
        }

        [Test]
        public void TransferData_PendingNextChunk_WarnsAndDoesNotRestart()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            // Manually place the session in the inter-chunk gap state without going through a
            // full multi-chunk sequence, to isolate this specific guard.
            SetPendingNextChunk(session, true);

            LogAssert.Expect(LogType.Warning, "[TsvrcDataSender] Transfer already in progress. Call CancelDataTransfer() first.");
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });

            Assert.AreEqual(0, session.OnChunkSequenceStartedCount);
        }

        [Test]
        public void TransferData_NullPlayerIds_WarnsAndDoesNotStart()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);

            LogAssert.Expect(LogType.Warning, "[TsvrcDataSender] Cannot transfer to null or empty player list.");
            session.TransferData("hello", null);

            Assert.AreEqual(0, session.OnChunkSequenceStartedCount);
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void TransferData_EmptyPlayerIds_WarnsAndDoesNotStart()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);

            LogAssert.Expect(LogType.Warning, "[TsvrcDataSender] Cannot transfer to null or empty player list.");
            session.TransferData("hello", new string[0]);

            Assert.AreEqual(0, session.OnChunkSequenceStartedCount);
        }

        [Test]
        public void TransferData_NullMessage_WarnsViaValidateMessageAndDoesNotStart()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);

            LogAssert.Expect(LogType.Warning, "[DataChunker] Cannot send empty message");
            session.TransferData(null, new[] { "TestOwner#" + OwnerPlayerId });

            Assert.AreEqual(0, session.OnChunkSequenceStartedCount);
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void TransferData_MessageTooLarge_LogsErrorAndDoesNotStart()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string tooLarge = BuildString(DataChunkerTestSubclass.MaxMessageSizeConst + 1);

            LogAssert.Expect(LogType.Error, $"[DataChunker] Message too large: {tooLarge.Length} chars (max {DataChunkerTestSubclass.MaxMessageSizeConst})");
            session.TransferData(tooLarge, new[] { "TestOwner#" + OwnerPlayerId });

            Assert.AreEqual(0, session.OnChunkSequenceStartedCount);
        }

        [Test]
        public void TransferData_Valid_SingleChunkMessage_SplitsToOneChunkAndSendsIt()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);

            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });

            Assert.AreEqual(1, GetCurrentChunkIndex(session));
            Assert.AreEqual(1, GetTotalChunksField(session));
            Assert.AreEqual(1, session.OnChunkSequenceStartedCount);
            CollectionAssert.AreEqual(new[] { "hello" }, session.OnDataChunkSendRequestedDataChunks);
            CollectionAssert.AreEqual(new[] { 1 }, session.OnDataChunkSendRequestedChunkIndexes);
            CollectionAssert.AreEqual(new[] { 1 }, session.OnDataChunkSendRequestedTotalChunks);
            Assert.IsTrue(session.IsProcessRunning());
        }

        [Test]
        public void TransferData_Valid_MultiChunkMessage_TotalChunksCorrectAndOnlyFirstChunkSent()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2 + 10);

            session.TransferData(data, new[] { "TestOwner#" + OwnerPlayerId });

            Assert.AreEqual(3, GetTotalChunksField(session));
            Assert.AreEqual(1, GetCurrentChunkIndex(session));
            Assert.AreEqual(1, session.OnDataChunkSendRequestedDataChunks.Count,
                "Only the first chunk is sent up front; later chunks are sent one at a time as each ready check completes.");
            Assert.AreEqual(DataChunkerTestSubclass.ChunkSizeConst, session.OnDataChunkSendRequestedDataChunks[0].Length);
        }

        [Test]
        public void TransferData_SecondTransferAfterFirstFullyCompletes_StartsCleanly()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("first", new[] { "TestOwner#" + OwnerPlayerId });
            session.SetReady(); // sole tracked player acks -> completes the single-chunk transfer

            Assert.IsFalse(session.IsProcessRunning());
            // TsvrcProcess.InternalCleanup unconditionally zeroes _ownerPlayerIdInt on every
            // stop/complete, before OnProcessCleanup even runs. In real Play Mode the next
            // StartProcess call harmlessly re-claims ownership via the real Networking.LocalPlayer;
            // in Edit Mode that's null, so a second, non-reentrant start needs re-seeding.
            SeedAsOwner(session);

            session.TransferData("second", new[] { "TestOwner#" + OwnerPlayerId });

            Assert.AreEqual(2, session.OnChunkSequenceStartedCount);
            CollectionAssert.AreEqual(new[] { "first", "second" }, session.OnDataChunkSendRequestedDataChunks);
            Assert.AreEqual(1, GetTotalChunksField(session));
        }
    }
}
