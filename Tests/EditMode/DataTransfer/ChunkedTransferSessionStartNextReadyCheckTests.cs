using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers _StartNextReadyCheck()'s own guard and its departure-filtering logic.
    // TsPlayer.GetAllPlayers() (called unconditionally, no early-return guard) returns an empty
    // array in Edit Mode, so this method can only be exercised taking the "every target
    // departed" branch here; the real "continues with remaining active targets" path needs a
    // genuine ClientSim player list and is covered in Play Mode instead.
    public class ChunkedTransferSessionStartNextReadyCheckTests : DataTransferTestBase
    {
        [Test]
        public void StartNextReadyCheck_PendingNextChunkFalse_IsNoOp()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            // _pendingNextChunk defaults to false: never entered the inter-chunk gap.

            Assert.DoesNotThrow(() => session._StartNextReadyCheck());

            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount);
            Assert.AreEqual(0, session.OnChunkSequenceStartedCount);
        }

        [Test]
        public void StartNextReadyCheck_InEditModeEveryTargetAppearsDeparted_StopsAndResetsAllState()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2);
            session.TransferData(data, new[] { ownerId });
            session.SetReady(); // completes chunk 1, enters the gap
            Assert.IsTrue(GetPendingNextChunk(session));

            session._StartNextReadyCheck();

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.AreEqual(0, session.OnChunkSequenceCompletedCount);
            Assert.IsFalse(GetPendingNextChunk(session));
            Assert.AreEqual(0, GetCurrentChunkIndex(session));
            CollectionAssert.AreEqual(new string[0], GetDataChunks(session));
            Assert.IsFalse(session.IsProcessRunning());
        }
    }
}
