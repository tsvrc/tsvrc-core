using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    // Covers chunk completion/stop lifecycle: single-chunk full completion, the inter-chunk gap
    // setup after a non-last chunk completes, stopping mid-chunk, and the reentrancy guards in
    // OnProcessCompleted/OnProcessStopped/OnProcessCleanup that protect a subscriber restarting
    // the transfer synchronously from inside a sequence hook.
    public class ChunkedTransferSessionChunkLifecycleTests : DataTransferTestBase
    {
        [Test]
        public void SingleChunkTransfer_OwnerAcks_CompletesAndClearsAllState()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });

            session.SetReady(); // sole tracked player acks the only chunk

            Assert.AreEqual(1, session.OnChunkSequenceCompletedCount);
            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount);
            Assert.AreEqual(0, GetCurrentChunkIndex(session));
            Assert.AreEqual(0, GetTotalChunksField(session));
            CollectionAssert.AreEqual(new string[0], GetDataChunks(session));
            Assert.IsFalse(GetPendingNextChunk(session));
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void OnProcessCompleted_LastChunk_DoesNotCaptureTargetPlayerIds()
        {
            // _targetPlayerIds is only meaningful for the inter-chunk gap; on the final chunk,
            // OnProcessCompleted returns before that assignment line.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });

            session.SetReady();

            CollectionAssert.AreEqual(new string[0], GetTargetPlayerIds(session));
        }

        [Test]
        public void MultiChunkTransfer_FirstChunkCompletes_SetsUpInterChunkGapCorrectly()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2);
            session.TransferData(data, new[] { ownerId });

            session.SetReady(); // owner acks chunk 1 of 2

            Assert.AreEqual(0, session.OnChunkSequenceCompletedCount, "Only the last chunk fires OnChunkSequenceCompleted.");
            Assert.AreEqual(2, GetCurrentChunkIndex(session), "Advanced to chunk 2.");
            Assert.AreEqual(2, GetTotalChunksField(session));
            Assert.IsTrue(GetPendingNextChunk(session));
            CollectionAssert.AreEqual(new[] { ownerId }, GetTargetPlayerIds(session),
                "Captured before PlayerTracker's cleanup clears the tracked list.");
            Assert.IsFalse(session.IsProcessRunning(), "Between chunks, the process is momentarily not running.");
        }

        [Test]
        public void MultiChunkTransfer_LastChunkCompletes_FiresOnChunkSequenceCompletedNotGapSetup()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2);
            session.TransferData(data, new[] { ownerId });
            session.SetReady(); // completes chunk 1, sets up the gap
            SetPendingNextChunk(session, false); // simulate the gap already having been consumed
            SetCurrentChunkIndex(session, 2); // already advanced by the gap setup above

            // Manually re-enter the ready check for chunk 2, mirroring what a real
            // _StartNextReadyCheck() call would have done, to isolate this test to the
            // last-chunk completion branch specifically (_StartNextReadyCheck()'s own
            // departure-filtering logic needs a real VRCPlayerApi list - Play Mode only).
            // Re-seed ownership: InternalCleanup zeroed _ownerPlayerIdInt when chunk 1's ready
            // check completed (see TransferData_SecondTransferAfterFirstFullyCompletes_StartsCleanly
            // for the full explanation); in real Play Mode this is a harmless re-claim via the
            // real Networking.LocalPlayer, but Edit Mode needs the reflection seed again.
            SeedAsOwner(session);
            session.StartReadyCheck(new[] { ownerId });
            session.SetReady(); // owner acks chunk 2 (the last one)

            Assert.AreEqual(1, session.OnChunkSequenceCompletedCount);
            Assert.AreEqual(0, GetCurrentChunkIndex(session));
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void StopMidChunk_FiresOnChunkSequenceStoppedAndClearsState()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            session.TransferData("hello", new[] { ownerId, "Other#1" }); // second player never acks

            session.StopReadyCheck();

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.AreEqual(0, session.OnChunkSequenceCompletedCount);
            Assert.AreEqual(0, GetCurrentChunkIndex(session));
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void OnChunkSequenceStopped_ReentrantRestartFromWithinHook_NewTransferStartsCleanly()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            session.TransferData("hello", new[] { ownerId, "Other#1" });
            session.OnChunkSequenceStoppedAction = () => session.TransferData("restarted", new[] { ownerId });

            session.StopReadyCheck();

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount, "Only the old transfer's stop.");
            Assert.AreEqual(2, session.OnChunkSequenceStartedCount, "Original start + reentrant restart's start.");
            Assert.IsTrue(session.IsProcessRunning());
            Assert.AreEqual(1, GetCurrentChunkIndex(session));
            CollectionAssert.AreEqual(new[] { "hello", "restarted" }, session.OnDataChunkSendRequestedDataChunks);
        }

        [Test]
        public void OnChunkSequenceCompleted_ReentrantRestartFromWithinHook_NewTransferStartsCleanly()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            session.TransferData("hello", new[] { ownerId });
            session.OnChunkSequenceCompletedAction = () => session.TransferData("restarted", new[] { ownerId });

            session.SetReady(); // completes the single-chunk transfer, triggering the reentrant restart

            Assert.AreEqual(1, session.OnChunkSequenceCompletedCount);
            Assert.AreEqual(2, session.OnChunkSequenceStartedCount);
            Assert.IsTrue(session.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "hello", "restarted" }, session.OnDataChunkSendRequestedDataChunks);
        }

        [Test]
        public void CancelRequestedFlag_ClearedAfterEveryCleanup_EvenOnNormalStop()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId, "Other#1" });

            session.StopReadyCheck();

            Assert.IsFalse(GetCancelRequested(session));
        }
    }
}
