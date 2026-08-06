using NUnit.Framework;
using Tsvrc.Session;

namespace Tsvrc.Tests.EditMode
{
    // Covers RankedGameSession.GetPlayerStatus for every RankedGamePlayerStatus value it can
    // return, including the Loading-vs-InLobby distinction (the same lobby membership reports
    // differently depending on CurrentState) and the NotInSession default.
    public class RankedGameSessionPlayerStatusTests : RankedGameSessionTestBase
    {
        [Test]
        public void GetPlayerStatus_UnknownPlayer_ReturnsNotInSession()
        {
            var h = CreateWiredSession();

            Assert.AreEqual(RankedGamePlayerStatus.NotInSession, h.Session.GetPlayerStatus("Ghost"));
        }

        [Test]
        public void GetPlayerStatus_LobbyMemberWhileIdle_ReturnsInLobby()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });

            Assert.AreEqual(RankedGamePlayerStatus.InLobby, h.Session.GetPlayerStatus("A"));
        }

        [Test]
        public void GetPlayerStatus_SameLobbyMemberOnceLoadingStarts_ReturnsLoadingNotInLobby()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();

            Assert.AreEqual(RankedGameSessionState.Loading, h.Session.CurrentState);
            Assert.AreEqual(RankedGamePlayerStatus.Loading, h.Session.GetPlayerStatus("A"));
        }

        [Test]
        public void GetPlayerStatus_GamePlayer_ReturnsPlaying()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            Assert.AreEqual(RankedGamePlayerStatus.Playing, h.Session.GetPlayerStatus("A"));
        }

        [Test]
        public void GetPlayerStatus_CompletedPlayer_ReturnsCompletedNotPlaying()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A", "B" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            h.ReadyCheck.BroadcastAddReadyPlayer("B");

            h.Session.AddCompletedPlayer("A");

            Assert.AreEqual(RankedGamePlayerStatus.Completed, h.Session.GetPlayerStatus("A"));
            Assert.AreEqual(RankedGamePlayerStatus.Playing, h.Session.GetPlayerStatus("B"));
        }

        [Test]
        public void GetPlayerStatus_PlayerNeverInLobby_ReturnsNotInSessionEvenDuringActiveGame()
        {
            // A late joiner (the Spectator case): they were never a LobbyPlayerIds member, so
            // they must not be mistaken for a Loading/InLobby participant just because a
            // session happens to be active for everyone else.
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            Assert.AreEqual(RankedGamePlayerStatus.NotInSession, h.Session.GetPlayerStatus("LateJoiner"));
        }
    }
}
