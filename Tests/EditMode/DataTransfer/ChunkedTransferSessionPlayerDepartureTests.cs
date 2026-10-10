using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers OnProcessUpdate's own addition on top of ReadyCheckProcess's poll tick: cancelling
    // the whole transfer if every tracked player leaves mid-chunk, since CheckAllPlayersReady
    // (called by the base ReadyCheckProcess.OnProcessUpdate first) returns early - and so never
    // completes - on an empty tracked list, which would otherwise stall the transfer forever.
    public class ChunkedTransferSessionPlayerDepartureTests : DataTransferTestBase
    {
        [Test]
        public void OnProcessUpdate_AllTrackedPlayersLeftMidChunk_CancelsTheTransfer()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId, "Other#1" });
            // Simulate everyone having left: clear the tracked list directly, isolating this test
            // to OnProcessUpdate's own reaction rather than the departure mechanics themselves.
            SetTrackedPlayerIds(session, new string[0]);

            ForceNextTickDueNow(session);
            session._TickProcessUpdate();

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void OnProcessUpdate_SomeTrackedPlayersRemain_DoesNotCancel()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            session.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId, "Other#1" });

            ForceNextTickDueNow(session);
            session._TickProcessUpdate();

            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount);
            Assert.IsTrue(session.IsProcessRunning());
        }
    }
}
