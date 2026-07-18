using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers SetReady's every branch: the _readyCheckActive gate, the owner-side direct
    // mutation path (both ready=true and ready=false), and the non-owner forwarding path.
    public class ReadyCheckProcessSetReadyTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void SetReady_ReadyCheckNotActive_IsNoOp()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            // Never started: _readyCheckActive is false by default.

            Assert.DoesNotThrow(() => tracker.SetReady());

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void SetReady_Owner_ProcessNotRunningButFlagStale_IsNoOp()
        {
            // _readyCheckActive can lag behind the true process state on the owner (per the
            // class's own doc comment); the owner path re-checks IsProcessRunning() explicitly.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            SetReadyCheckActive(tracker, true);
            Assert.IsFalse(tracker.IsProcessRunning());

            tracker.SetReady();

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void SetReady_Owner_NotTracked_IsNoOp()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "Other#1" });

            tracker.SetReady();

            Assert.IsFalse(InvokeIsPlayerReady(tracker, "TestOwner#" + OwnerPlayerId));
            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void SetReady_Owner_AlreadyReady_SecondCallIsNoOp()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            tracker.StartReadyCheck(new[] { ownerId, "Other#1" });

            tracker.SetReady();
            Assert.IsTrue(InvokeIsPlayerReady(tracker, ownerId));

            tracker.SetReady();

            CollectionAssert.AreEqual(new[] { ownerId }, GetReadyPlayerIds(tracker),
                "A second SetReady() call must not add a duplicate entry.");
        }

        [Test]
        public void SetReady_Owner_SinglePlayerCheck_MarkingSelfReadyAutoCompletes()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "TestOwner#" + OwnerPlayerId });

            tracker.SetReady();

            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
            Assert.IsFalse(tracker.IsProcessRunning());
        }

        [Test]
        public void SetReady_Owner_MultiPlayerCheck_StaysRunningUntilEveryoneReady()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            tracker.StartReadyCheck(new[] { ownerId, "Remote#1", "Remote#2" });

            tracker.SetReady();
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);

            tracker.BroadcastAddReadyPlayer("Remote#1");
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);

            tracker.BroadcastAddReadyPlayer("Remote#2");
            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void SetReady_False_Owner_RemovesReadyPlayerAndNeverCompletes()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            tracker.StartReadyCheck(new[] { ownerId, "Remote#1" });
            tracker.SetReady();
            Assert.IsTrue(InvokeIsPlayerReady(tracker, ownerId));

            tracker.SetReady(false);

            Assert.IsFalse(InvokeIsPlayerReady(tracker, ownerId));
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount,
                "Un-readying can never be what completes a check.");
        }

        [Test]
        public void SetReady_False_Owner_NotCurrentlyReady_IsNoOp()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "TestOwner#" + OwnerPlayerId });

            Assert.DoesNotThrow(() => tracker.SetReady(false));

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }

        [Test]
        public void SetReady_NonOwner_Ready_ForwardsButLocalSelfDispatchIsRejected()
        {
            // The Editor proxy's SendCustomNetworkEvent ignores NetworkEventTarget and
            // reflection-invokes the named method on the same instance synchronously
            // (see PlayerTrackerAddRemoveTests for the same fact applied to PlayerTracker),
            // so the forwarded BroadcastAddReadyPlayer call really does execute here and
            // must be rejected by its own owner guard.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SetReadyCheckActive(tracker, true);
            // Deliberately not seeded as owner.

            Assert.DoesNotThrow(() => tracker.SetReady());

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void SetReady_NonOwner_Unready_ForwardsButLocalSelfDispatchIsRejected()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SetReadyCheckActive(tracker, true);

            Assert.DoesNotThrow(() => tracker.SetReady(false));

            CollectionAssert.IsEmpty(GetReadyPlayerIds(tracker));
        }
    }
}
