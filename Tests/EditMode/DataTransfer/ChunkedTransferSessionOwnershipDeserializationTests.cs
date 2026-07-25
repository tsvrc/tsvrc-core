using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers ChunkedTransferSession's OnOwnerAbandonedProcess and OnDeserialization overrides.
    // PlayerTracker.OnOwnerAbandonedProcess's own scan (reached via the base call at the end of
    // ChunkedTransferSession's override) only reaches TsPlayer.GetAllPlayers() when
    // _trackedPlayerIds is non-empty - see PlayerTrackerAbandonmentTests.cs. In every branch
    // below, _trackedPlayerIds is already empty by the time the base call runs (either the ready
    // check was just stopped, or it was never running), so the whole chain stays Edit-Mode-safe.
    //
    // OnDeserialization's first branch requires Networking.IsOwner(gameObject) to be true.
    // It is unconditionally true in this environment, both here in Edit Mode and under
    // ClientSim in Play Mode, so that branch is reachable and tested directly below.
    public class ChunkedTransferSessionOwnershipDeserializationTests : DataTransferTestBase
    {
        [Test]
        public void OnOwnerAbandonedProcess_ProcessRunning_StopsTheActiveReadyCheck()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            PrivateFieldAccess.SetField(session, "_isRunning", true);

            PrivateFieldAccess.InvokeInstance(session, "OnOwnerAbandonedProcess");

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void OnOwnerAbandonedProcess_PendingNextChunk_ResetsAndBroadcastsStopped()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            SetPendingNextChunk(session, true);
            SetCurrentChunkIndex(session, 2);
            SetTotalChunksField(session, 3);

            PrivateFieldAccess.InvokeInstance(session, "OnOwnerAbandonedProcess");

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.IsFalse(GetPendingNextChunk(session));
            Assert.AreEqual(0, GetCurrentChunkIndex(session));
        }

        [Test]
        public void OnOwnerAbandonedProcess_NeitherRunningNorPending_IsANoOpForChunkState()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            // Idle: never transferred anything.

            Assert.DoesNotThrow(() => PrivateFieldAccess.InvokeInstance(session, "OnOwnerAbandonedProcess"));

            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount);
        }

        [Test]
        public void OnDeserialization_ZombieProcessSignature_StopsTheReadyCheck()
        {
            // IsProcessOwner()==true, IsProcessRunning()==true, _dataChunks empty, not pending -
            // the exact signature OnDeserialization's second branch treats as a zombie process
            // (an _isRunning=true packet arrived for a run whose owner-only _dataChunks was
            // never populated locally) and recovers from via StopReadyCheck().
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            PrivateFieldAccess.SetField(session, "_isRunning", true);

            session.OnDeserialization();

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.IsFalse(session.IsProcessRunning());
        }

        [Test]
        public void OnDeserialization_DataChunksPopulated_ZombieCheckDoesNotFire()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            PrivateFieldAccess.SetField(session, "_isRunning", true);
            SetDataChunks(session, new[] { "chunk1" }); // a legitimate, populated owner run

            session.OnDeserialization();

            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount);
        }

        [Test]
        public void OnDeserialization_PendingNextChunkTrue_ZombieCheckDoesNotFire()
        {
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            PrivateFieldAccess.SetField(session, "_isRunning", true);
            SetPendingNextChunk(session, true);

            session.OnDeserialization();

            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount);
        }

        [Test]
        public void OnDeserialization_LatePacketRaceSignature_StopsAndClearsThePendingGap()
        {
            // Networking.IsOwner(gameObject) is unconditionally true in this environment, both
            // here in Edit Mode and under ClientSim in Play Mode, so this branch (recovery from
            // a late InternalCleanup packet arriving after OnOwnerAbandonedProcess already ran)
            // is reachable and tested directly here.
            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            SeedAsOwner(session);
            SetPendingNextChunk(session, true);
            // _isRunning stays false, _dataChunks stays empty - the exact late-packet signature.

            session.OnDeserialization();

            Assert.AreEqual(1, session.OnChunkSequenceStoppedCount);
            Assert.IsFalse(GetPendingNextChunk(session));
        }
    }
}
