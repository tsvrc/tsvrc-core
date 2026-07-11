using NUnit.Framework;
using Tsvrc.Tracking;

namespace Tsvrc.Tests.Editor
{
    // Covers CancelDataTransfer at each of its four documented windows: idle (no-op), mid-chunk
    // (routes through StopReadyCheck), the inter-chunk gap (_pendingNextChunk), and the narrow
    // post-completion/pre-cleanup window (sets _cancelRequested instead of acting immediately).
    public class ChunkedTransferSessionCancelTests : DataTransferTestBase
    {
        [Test]
        public void CancelDataTransfer_Idle_IsATrueNoOp()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);

            Assert.DoesNotThrow(() => session.CancelDataTransfer());

            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount);
            Assert.AreEqual(0, session.OnChunkSequenceCompletedCount);
        }

        [Test]
        public void CancelDataTransfer_MidChunk_RoutesThroughStopReadyCheck()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId, "Other#1" });

            session.CancelDataTransfer();

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.IsFalse(session.IsProcessRunning());
            Assert.AreEqual(0, GetCurrentChunkIndex(session));
        }

        [Test]
        public void CancelDataTransfer_InterChunkGap_ImmediatelyResetsAndBroadcastsStopped()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2);
            session.TransferData(data, new[] { ownerId });
            session.SetReady(); // completes chunk 1, enters the gap (_pendingNextChunk=true)
            Assert.IsTrue(GetPendingNextChunk(session));

            session.CancelDataTransfer();

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.AreEqual(0, session.OnChunkSequenceCompletedCount);
            Assert.IsFalse(GetPendingNextChunk(session));
            Assert.AreEqual(0, GetCurrentChunkIndex(session));
        }

        [Test]
        public void CancelDataTransfer_PostCompletionWindow_LastChunk_IsATrueNoOp()
        {
            // "the transfer is done, just return" per the source comment: cancelling after the
            // very last chunk already completed changes nothing.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId }); // single chunk
            session.OnChunkSequenceCompletedAction = () => session.CancelDataTransfer();

            session.SetReady(); // completes the only chunk, triggering the action above

            Assert.AreEqual(1, session.OnChunkSequenceCompletedCount);
            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount,
                "A cancel after the transfer's own last chunk already completed must not also stop it.");
            Assert.IsFalse(GetCancelRequested(session));
        }

        [Test]
        public void CancelDataTransfer_PostCompletionWindow_NonLastChunk_RedirectsCleanupToStopInsteadOfAdvancing()
        {
            // The one window CancelDataTransfer can't act on directly: chunk 1's ready check has
            // already completed (base.OnProcessCompleted's own broadcast is mid-flight) but
            // ChunkedTransferSession's own OnProcessCompleted continuation (the isLastChunk
            // check, _targetPlayerIds capture) hasn't resumed yet. Reached here via a listener on
            // ReadyCheckProcess's own OnReadyCheckCompletedEvent, which fires from inside that
            // exact window.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2); // 2 chunks
            session.TransferData(data, new[] { ownerId });
            session.TsSubscribe(session, ReadyCheckProcess.OnReadyCheckCompletedEvent, nameof(session._CancelDuringReadyCheckCompletedEvent));

            session.SetReady(); // completes chunk 1 (not the last), triggering the subscriber above

            Assert.AreEqual(0, session.OnChunkSequenceCompletedCount, "isLastChunk was captured as false before the cancel; must never fire.");
            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount,
                "_cancelRequested redirects OnProcessCleanup to stop the whole sequence instead of advancing to chunk 2.");
            Assert.IsFalse(GetPendingNextChunk(session));
            Assert.AreEqual(0, GetCurrentChunkIndex(session));
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void TransferData_RestartFromWithinNonLastChunkReadyCheckCompletedWindow_OldTransferBailsCleanlyAndNewOneRuns()
        {
            // The restart counterpart to CancelDataTransfer_PostCompletionWindow_NonLastChunk
            // above: a subscriber to OnReadyCheckCompletedEvent (fires for every chunk, not just
            // the last) calls TransferData() again from that same narrow window - strictly before
            // ChunkedTransferSession's own OnProcessCompleted() continuation (isLastChunk check,
            // _targetPlayerIds capture, OnProcessCleanup's chunk-advance scheduling) resumes.
            // Unlike the already-tested last-chunk restart (OnChunkSequenceCompletedAction, a
            // later/different hook that only fires once per whole transfer), this exercises the
            // reentrant-restart guards from the EARLIEST point a subscriber can reach mid-transfer.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2); // 2 chunks
            session.TransferData(data, new[] { ownerId });
            session.RestartPlayerIds = new[] { ownerId };
            session.TsSubscribe(session, ReadyCheckProcess.OnReadyCheckCompletedEvent, nameof(session._RestartDuringReadyCheckCompletedEvent));

            session.SetReady(); // completes chunk 1 (not the last), triggering the reentrant restart

            // The old (2-chunk) transfer must never reach its own gap-setup or completion logic:
            // IsProcessRunning() is true by the time its OnProcessCompleted continuation resumes
            // (the new transfer already started), so it bails out via the documented guard.
            Assert.AreEqual(0, session.OnChunkSequenceCompletedCount, "The old transfer's isLastChunk=false path must never fire completed.");
            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount, "The old transfer must not be reported as stopped either - it was superseded, not stopped.");
            CollectionAssert.AreEqual(new string[0], GetTargetPlayerIds(session),
                "The old transfer's gap-setup (_targetPlayerIds capture) must not run once a new transfer is already active.");

            // The new (restarted) transfer must be running cleanly on chunk 1, not corrupted by
            // the old transfer's state.
            Assert.IsTrue(session.IsProcessRunning());
            Assert.AreEqual(1, GetCurrentChunkIndex(session));
            Assert.AreEqual(2, session.OnChunkSequenceStartedCount, "Original transfer's start + the restarted transfer's start.");
            // OnDataChunkSendRequestedDataChunks records each CHUNK's content, not the raw input:
            // the old transfer's chunk 1 is only the first CHUNK_SIZE chars of the 2-chunk data.
            string firstChunkOfOldTransfer = data.Substring(0, DataChunkerTestSubclass.ChunkSizeConst);
            CollectionAssert.AreEqual(new[] { firstChunkOfOldTransfer, "restarted" }, session.OnDataChunkSendRequestedDataChunks);

            // The new transfer must still be able to complete normally afterward, proving no
            // lingering state (_pendingNextChunk, _cancelRequested, stale _currentChunkIndex)
            // from the old transfer leaked through. RestartPlayerIds is already null at this
            // point (the double's one-shot guard cleared it after firing once), so this ack
            // does not trigger yet another reentrant restart - it lets the restarted transfer
            // actually reach its own OnChunkSequenceCompleted.
            session.SetReady();
            Assert.AreEqual(1, session.OnChunkSequenceCompletedCount);
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void CancelDataTransfer_CalledTwiceMidChunk_SecondCallIsHarmless()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId, "Other#1" });

            session.CancelDataTransfer();
            Assert.DoesNotThrow(() => session.CancelDataTransfer());

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount, "The second call must be the idle no-op, not a duplicate stop.");
        }
    }
}
