using NUnit.Framework;
using Tsvrc.Session;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers RankedGameSession.LastEndReason for every RankedGameSessionEndReason value,
    // asserting it is set correctly and stably before the corresponding event fires - same
    // snapshot-timing guarantee already proven for LastEndedGamePlayerIds.
    public class RankedGameSessionEndReasonTests : RankedGameSessionTestBase
    {
        [Test]
        public void AllPlayersCompleted_SetsLastEndReasonBeforeOnSessionEndedFires()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            h.Session.OnSessionEndedAction = () =>
                Assert.AreEqual(RankedGameSessionEndReason.AllPlayersCompleted, h.Session.LastEndReason,
                    "Must already be set by the time the hook runs, not only after TsEmit.");

            h.Session.AddCompletedPlayer("A");

            Assert.AreEqual(RankedGameSessionEndReason.AllPlayersCompleted, h.Session.LastEndReason);
        }

        [Test]
        public void AllPlayersLeft_SetsLastEndReason()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            h.Session.RemoveGamePlayer("A");

            Assert.AreEqual(RankedGameSessionEndReason.AllPlayersLeft, h.Session.LastEndReason);
        }

        [Test]
        public void TimerExpired_SetsLastEndReason()
        {
            var h = CreateWiredSession();
            h.Session.SetTimerDuration(1000);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            int start = PrivateFieldAccess.GetField<int>(h.Timer, "_startServerTimeMs");
            PrivateFieldAccess.SetField(h.Timer, "_startServerTimeMs", start - 1000);

            ForceNextTickDueNow(h.Timer);
            h.Timer._TickProcessUpdate();

            Assert.AreEqual(RankedGameSessionEndReason.TimerExpired, h.Session.LastEndReason);
        }

        [Test]
        public void Stopped_StopSessionDuringInGame_SetsLastEndReason()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            h.Session.StopSession();

            Assert.AreEqual(RankedGameSessionEndReason.Stopped, h.Session.LastEndReason);
        }

        [Test]
        public void Stopped_StopSessionDuringLoading_SetsLastEndReason()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();

            h.Session.StopSession();

            Assert.AreEqual(RankedGameSessionEndReason.Stopped, h.Session.LastEndReason);
        }

        [Test]
        public void EmptyLobbyDuringLoading_LastRemainingPlayerLeaves_SetsLastEndReason()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();

            h.ReadyCheck.RemoveTrackedPlayers(new[] { "A" });

            Assert.AreEqual(RankedGameSessionEndReason.EmptyLobbyDuringLoading, h.Session.LastEndReason);
        }

        [Test]
        public void LastEndReason_DefaultsToNoneBeforeAnySessionEnds()
        {
            var h = CreateWiredSession();

            Assert.AreEqual(RankedGameSessionEndReason.None, h.Session.LastEndReason);
        }
    }
}
