using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // Covers ChunkedTransferSession's CanAcceptTrackedPlayerAdditions override: a player added
    // while a chunk is actively in flight was never sent that chunk's already-broadcast data, so
    // BroadcastDataChunkReceived's own playerIds check rejects every chunk for them, and they can
    // never call SetReady(). Since CheckAllPlayersReady requires every tracked player to be
    // ready, an unrejected addition would permanently stall the transfer.
    public class ChunkedTransferSessionAddTrackedPlayersTests : DataTransferTestBase
    {
        [Test]
        public void AddTrackedPlayers_WhileChunkActivelyInFlight_RejectedWithWarning()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId }); // chunk 1 actively in flight
            Assert.IsTrue(session.IsProcessRunning());

            LogAssert.Expect(LogType.Warning, "[TsDataSender] Cannot add tracked players while a transfer is in progress.");
            session.AddTrackedPlayers(new[] { "LateJoiner#5" });

            CollectionAssert.DoesNotContain(GetTrackedPlayerIds(session), "LateJoiner#5");
        }

        [Test]
        public void AddTrackedPlayers_RejectedMidChunk_TransferStillCompletesNormallyAfterwards()
        {
            // A rejected addition must not leave the ready check stuck waiting on a late joiner
            // that was never actually tracked: the original single tracked player's own ACK
            // still completes the transfer normally.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });
            LogAssert.Expect(LogType.Warning, "[TsDataSender] Cannot add tracked players while a transfer is in progress.");
            session.AddTrackedPlayers(new[] { "LateJoiner#5" });

            session.SetReady(); // the original owner's own ack

            Assert.IsFalse(session.IsProcessRunning(), "The transfer must complete, not stall forever.");
            Assert.AreEqual(1, session.OnChunkSequenceCompletedCount);
        }

        [Test]
        public void AddTrackedPlayers_DuringInterChunkGap_SilentlyRejectedByThePreExistingNotRunningGuard()
        {
            // During the gap _isRunning is false, so BroadcastAddTrackedPlayers's own
            // IsProcessRunning() guard rejects the call before CanAcceptTrackedPlayerAdditions()
            // is ever reached - silently, with no warning, unlike the actively-in-flight case in
            // AddTrackedPlayers_WhileChunkActivelyInFlight_RejectedWithWarning above.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2);
            session.TransferData(data, new[] { ownerId });
            session.SetReady(); // completes chunk 1, enters the inter-chunk gap
            Assert.IsTrue(GetPendingNextChunk(session));

            session.AddTrackedPlayers(new[] { "LateJoiner#5" });

            CollectionAssert.DoesNotContain(GetTargetPlayerIds(session), "LateJoiner#5");
        }

        [Test]
        public void BroadcastAddTrackedPlayers_CalledDirectlyMidChunk_RejectedWithWarning()
        {
            // The remote-caller path: a non-owner's AddTrackedPlayers() call never touches this
            // instance's AddTrackedPlayers at all - it routes straight to BroadcastAddTrackedPlayers
            // via SendCustomNetworkEvent(Owner, ...). This test drives that entry point directly
            // (as the network dispatch would) to confirm the guard rejects it too, not just the
            // owner-local AddTrackedPlayers() call path exercised by the test above.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId }); // chunk 1 actively in flight
            Assert.IsTrue(session.IsProcessRunning());

            LogAssert.Expect(LogType.Warning, "[TsDataSender] Cannot add tracked players while a transfer is in progress.");
            session.BroadcastAddTrackedPlayers(new[] { "LateJoiner#5" });

            CollectionAssert.DoesNotContain(GetTrackedPlayerIds(session), "LateJoiner#5");
        }

        [Test]
        public void AddTrackedPlayers_NoActiveTransfer_DoesNotHitTheTransferInProgressGuard()
        {
            // _currentChunkIndex defaults to 0 (no transfer ever started), so
            // CanAcceptTrackedPlayerAdditions must not reject here. BroadcastAddTrackedPlayers's
            // own deeper not-running rejection (silent, no warning) still applies independently.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);

            Assert.DoesNotThrow(() => session.AddTrackedPlayers(new[] { "Other#1" }));
        }

        [Test]
        public void AddTrackedPlayers_AfterTransferFullyCompletes_DoesNotHitTheTransferInProgressGuard()
        {
            // _currentChunkIndex resets to 0 once the whole transfer finishes, so the guard
            // must not leak into blocking unrelated, later use of the tracker.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });
            session.SetReady(); // completes the single-chunk transfer
            Assert.AreEqual(0, GetCurrentChunkIndex(session));

            Assert.DoesNotThrow(() => session.AddTrackedPlayers(new[] { "Other#1" }));
        }
    }
}
