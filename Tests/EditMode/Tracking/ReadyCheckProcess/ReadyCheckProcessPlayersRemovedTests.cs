using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers OnTrackingPlayersRemoved on the owner: the batch removal of ready players among the
    // untracked set, and the re-check-completion-even-for-non-ready-removals behavior documented
    // on the source. The non-owner half of its owner-only guard lives in
    // ReadyCheckProcessNotOwnerPlayModeTests, since Edit Mode can't produce a non-owner.
    public class ReadyCheckProcessPlayersRemovedTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void OnTrackingPlayersRemoved_Owner_RemovesReadyPlayersAmongRemoved()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            // Four tracked, two ready ("A","B"), removing one ready + one never-ready ("A","C")
            // still leaves a non-ready player ("D") behind, so the removal itself must not
            // also trigger completion (which would clear _readyPlayerIds and defeat the
            // assertion below) - unlike the companion completes-for-the-rest test.
            tracker.StartReadyCheck(new[] { "A", "B", "C", "D" });
            tracker.BroadcastAddReadyPlayer("A");
            tracker.BroadcastAddReadyPlayer("B");

            tracker.RemoveTrackedPlayers(new[] { "A", "C" });

            CollectionAssert.AreEqual(new[] { "B" }, GetReadyPlayerIds(tracker),
                "Removed-and-was-ready 'A' must be dropped; 'C' was never ready so there is nothing to drop for it.");
            CollectionAssert.AreEqual(new[] { "B", "D" }, GetTrackedPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount, "'D' is still tracked and not ready.");
        }

        [Test]
        public void OnTrackingPlayersRemoved_Owner_RemovingOnlyNonReadyTrackedPlayer_CompletesForTheRest()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            tracker.BroadcastAddReadyPlayer("A");
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);

            tracker.RemoveTrackedPlayers(new[] { "B" });

            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount,
                "Removing the only non-ready tracked player must trigger completion for the rest.");
        }

        [Test]
        public void OnTrackingPlayersRemoved_Owner_BatchRemoval_MultipleReadyPlayersInOnePass()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B", "C", "D" });
            tracker.BroadcastAddReadyPlayer("A");
            tracker.BroadcastAddReadyPlayer("B");
            tracker.BroadcastAddReadyPlayer("C");

            tracker.RemoveTrackedPlayers(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "C" }, GetReadyPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount, "'D' is still untracked-ready; must not complete.");
        }

        [Test]
        public void OnTrackingPlayersRemoved_Owner_NoneOfTheRemovedWereReady_StillReChecksHarmlessly()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B", "C" });
            tracker.BroadcastAddReadyPlayer("A");

            Assert.DoesNotThrow(() => tracker.RemoveTrackedPlayers(new[] { "C" }));

            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);
        }
    }
}
