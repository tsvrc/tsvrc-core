using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    // Direct tests of the two [NetworkCallable] receivers: running/owner/null/empty guards and
    // the tracked/already-ready filters. NetworkCalling.CallingPlayer reads null outside a real
    // dispatch, so the self-only "caller must match playerId" guard here is always skipped in
    // this environment: a direct call for an arbitrary playerId is indistinguishable, from this
    // class's own guards' point of view, from that id's real owner forwarding their own
    // SetReady() call, which is what makes a direct call usable to simulate a remote player's
    // own request landing at the owner.
    public class ReadyCheckProcessBroadcastReadyTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void BroadcastAddReadyPlayer_NotRunning_Rejected()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            // Never started.

            tracker.BroadcastAddReadyPlayer("TestOwner#" + OwnerPlayerId);

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastAddReadyPlayer_NotOwner_Rejected()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });
            PrivateFieldAccess.SetField(tracker, "_localPlayerIdInt", OwnerPlayerId + 1);

            tracker.BroadcastAddReadyPlayer("A");

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastAddReadyPlayer_NullOrEmpty_IsNoOp()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });

            Assert.DoesNotThrow(() => tracker.BroadcastAddReadyPlayer(null));
            Assert.DoesNotThrow(() => tracker.BroadcastAddReadyPlayer(""));

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastAddReadyPlayer_NotTracked_Rejected()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });

            tracker.BroadcastAddReadyPlayer("Z");

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastAddReadyPlayer_AlreadyReady_SecondCallIsNoOp()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });

            tracker.BroadcastAddReadyPlayer("A");
            tracker.BroadcastAddReadyPlayer("A");

            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastAddReadyPlayer_SinglePlayerCheck_ChecksCompletionImmediately()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });

            tracker.BroadcastAddReadyPlayer("A");

            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void BroadcastAddReadyPlayer_MultiPlayerCheck_DoesNotCompleteUntilAllReady()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });

            tracker.BroadcastAddReadyPlayer("A");
            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);

            tracker.BroadcastAddReadyPlayer("B");
            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void BroadcastAddReadyPlayer_DirectCallForArbitraryPlayerId_SucceedsBecauseCallingPlayerIsAlwaysNullOutsideRealDispatch()
        {
            // Simulates a remote player's own SetReady() request being processed by the
            // owner: NetworkCalling.CallingPlayer is null in this environment (see the Play
            // Mode diagnostic tests), so the "caller must equal playerId" guard is skipped
            // and any tracked, not-yet-ready id can be marked ready by a direct call, exactly
            // as it would be if a genuine remote client's forwarded call had arrived.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "RemoteA#1", "RemoteB#2" });

            tracker.BroadcastAddReadyPlayer("RemoteA#1");

            CollectionAssert.Contains(GetReadyPlayerIds(tracker), "RemoteA#1");
        }

        [Test]
        public void BroadcastAddReadyPlayer_FourDistinctPlayers_CompletesExactlyOnceWhenLastOneReadies()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "P1", "P2", "P3", "P4" });

            tracker.BroadcastAddReadyPlayer("P1");
            tracker.BroadcastAddReadyPlayer("P2");
            tracker.BroadcastAddReadyPlayer("P3");
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);

            tracker.BroadcastAddReadyPlayer("P4");

            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void BroadcastRemoveReadyPlayer_NotRunning_Rejected()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            SetTrackedPlayerIds(tracker, new[] { "A" });
            SetReadyPlayerIds(tracker, new[] { "A" });

            tracker.BroadcastRemoveReadyPlayer("A");

            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastRemoveReadyPlayer_NotOwner_Rejected()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A");
            PrivateFieldAccess.SetField(tracker, "_localPlayerIdInt", OwnerPlayerId + 1);

            tracker.BroadcastRemoveReadyPlayer("A");

            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastRemoveReadyPlayer_NullOrEmpty_IsNoOp()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            // Two tracked players, only one ready: keeps the check running so this test
            // observes the guard's own no-op, not a completion-triggered cleanup clearing
            // _readyPlayerIds out from under the assertion.
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A");

            Assert.DoesNotThrow(() => tracker.BroadcastRemoveReadyPlayer(null));
            Assert.DoesNotThrow(() => tracker.BroadcastRemoveReadyPlayer(""));

            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastRemoveReadyPlayer_NotCurrentlyReady_IsNoOp()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A" });

            Assert.DoesNotThrow(() => tracker.BroadcastRemoveReadyPlayer("A"));

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void BroadcastRemoveReadyPlayer_Valid_RemovesAndNeverAutoCompletes()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A");

            tracker.BroadcastRemoveReadyPlayer("A");

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);
        }
    }
}
