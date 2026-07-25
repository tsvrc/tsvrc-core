using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers CheckAllPlayersReady's own edge cases (empty tracked set) and the OnProcessUpdate
    // poll-tick path specifically - the safety net that re-evaluates readiness on a fixed
    // 0.5s cadence regardless of what (if anything) last triggered a check, and which only
    // ever runs on the process owner.
    public class ReadyCheckProcessCompletionTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void CheckAllPlayersReady_NoTrackedPlayers_NeverCompletesEvenWhenPolled()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new string[0]);

            ForceNextTickDueNow(tracker);
            tracker._TickProcessUpdate();

            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);
            Assert.IsTrue(tracker.IsProcessRunning());
        }

        [Test]
        public void OnProcessUpdate_PollTick_CompletesWhenAllReadyDespiteNoTriggeringCall()
        {
            // Simulates readiness becoming complete without an intervening SetReady/
            // BroadcastAddReadyPlayer call (e.g. a delayed sync catch-up writing
            // _readyPlayerIds directly) - only the periodic poll tick catches this.
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            SetReadyPlayerIds(tracker, new[] { "A", "B" });
            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);

            ForceNextTickDueNow(tracker);
            tracker._TickProcessUpdate();

            Assert.AreEqual(1, tracker.OnReadyCheckCompletedCount);
        }

        [Test]
        public void OnProcessUpdate_PollTick_NotAllReady_StaysRunning()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            SetReadyPlayerIds(tracker, new[] { "A" });

            ForceNextTickDueNow(tracker);
            tracker._TickProcessUpdate();

            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);
            Assert.IsTrue(tracker.IsProcessRunning());
        }

        [Test]
        public void OnProcessUpdate_NonOwnerTick_NeverPollsForCompletion()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartReadyCheck(new[] { "A", "B" });
            SetReadyPlayerIds(tracker, new[] { "A", "B" });
            // Flip away from owner without stopping - _TickProcessUpdate's own guard must
            // stop the loop and never reach OnProcessUpdate/CheckAllPlayersReady.
            PrivateFieldAccess.SetField(tracker, "_localPlayerIdInt", OwnerPlayerId + 1);

            ForceNextTickDueNow(tracker);
            tracker._TickProcessUpdate();

            Assert.AreEqual(0, tracker.OnReadyCheckCompletedCount);
        }
    }
}
