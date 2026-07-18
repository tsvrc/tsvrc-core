using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // Covers AddLobbyPlayer/RemoveLobbyPlayer and the _OnLobbyPlayersAdded/
    // _OnLobbyPlayersRemoved handlers that mirror the lobby tracker's own events.
    public class RankedGameSessionLobbyPlayerTests : RankedGameSessionTestBase
    {
        [Test]
        public void AddLobbyPlayer_TrackerNotRunning_LogsErrorAndNoOps()
        {
            var h = CreateWiredSession();

            LogAssert.Expect(LogType.Error,
                "[RankedGameSessionTestSubclass] AddLobbyPlayer: lobby tracker is not running. Call StartLobbyTracking() first.");
            h.Session.AddLobbyPlayer("A");

            CollectionAssert.AreEqual(new string[0], h.Session.LobbyPlayerIds);
        }

        [Test]
        public void AddLobbyPlayer_TrackerRunning_AddsPlayerAndFiresHookThenEvent()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.Session.TsSubscribe(h.Session, Tsvrc.Session.RankedGameSession.OnLobbyPlayerAddedEvent,
                nameof(h.Session._OnLobbyPlayerAddedEventReceived));

            h.Session.AddLobbyPlayer("A");

            CollectionAssert.AreEqual(new[] { "A" }, h.Session.LobbyPlayerIds);
            CollectionAssert.AreEqual(new[] { "A" }, h.Session.LastAddedLobbyPlayerIds);
            Assert.AreEqual(1, h.Session.OnLobbyPlayerAddedCount);
            CollectionAssert.AreEqual(new[] { "Hook:OnLobbyPlayerAdded", "Event:OnLobbyPlayerAddedEvent" }, h.Session.CallLog);
        }

        [Test]
        public void RemoveLobbyPlayer_TrackerNotRunning_LogsErrorAndNoOps()
        {
            var h = CreateWiredSession();

            LogAssert.Expect(LogType.Error,
                "[RankedGameSessionTestSubclass] RemoveLobbyPlayer: lobby tracker is not running. Call StartLobbyTracking() first.");
            h.Session.RemoveLobbyPlayer("A");
        }

        [Test]
        public void RemoveLobbyPlayer_TrackerRunning_RemovesPlayerAndFiresHookThenEvent()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.CallLog.Clear();
            h.Session.TsSubscribe(h.Session, Tsvrc.Session.RankedGameSession.OnLobbyPlayerRemovedEvent,
                nameof(h.Session._OnLobbyPlayerRemovedEventReceived));

            h.Session.RemoveLobbyPlayer("A");

            CollectionAssert.AreEqual(new string[0], h.Session.LobbyPlayerIds);
            CollectionAssert.AreEqual(new[] { "A" }, h.Session.LastRemovedLobbyPlayerIds);
            Assert.AreEqual(1, h.Session.OnLobbyPlayerRemovedCount);
            CollectionAssert.AreEqual(new[] { "Hook:OnLobbyPlayerRemoved", "Event:OnLobbyPlayerRemovedEvent" }, h.Session.CallLog);
        }

        [Test]
        public void AddLobbyPlayer_DuringActiveSession_StillWorksIndependentlyOfSessionState()
        {
            // AddLobbyPlayer/RemoveLobbyPlayer have no CurrentState guard - the lobby
            // roster for the NEXT session can keep changing while the current one is
            // Loading/InGame.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            Assert.AreEqual(Tsvrc.Session.RankedGameSessionState.InGame, h.Session.CurrentState);

            h.Session.AddLobbyPlayer("B");

            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.LobbyPlayerIds);
        }

        [Test]
        public void RemoveLobbyPlayer_DuringActiveSession_StillWorksIndependentlyOfSessionState()
        {
            // Mirrors AddLobbyPlayer_DuringActiveSession above for RemoveLobbyPlayer -
            // removing player B from the lobby (queued for the NEXT round) must not
            // touch the CURRENT round's already-locked-in game roster (still just A).
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A", "B" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            h.ReadyCheck.BroadcastAddReadyPlayer("B");
            Assert.AreEqual(Tsvrc.Session.RankedGameSessionState.InGame, h.Session.CurrentState);

            h.Session.RemoveLobbyPlayer("B");

            CollectionAssert.AreEqual(new[] { "A" }, h.Session.LobbyPlayerIds);
            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.GamePlayerIds,
                "Removing from the lobby must not retroactively change the current round's already-started game roster.");
        }

        [Test]
        public void LobbyPlayersAdded_BatchOfMultiplePlayers_AllForwardedInHookArgsAndLastAddedIds()
        {
            // AddLobbyPlayer only ever adds one player at a time by its own signature,
            // but multiple players can join a public lobby within the same instant in
            // the real world - driven directly through the lobby tracker here (the
            // same entry point AddLobbyPlayer itself uses under the hood) to prove
            // _OnLobbyPlayersAdded forwards the FULL batch, not just a single id.
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();

            h.LobbyTracker.AddTrackedPlayers(new[] { "A", "B", "C" });

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, h.Session.LastAddedLobbyPlayerIds);
            Assert.AreEqual(1, h.Session.OnLobbyPlayerAddedCount);
            Assert.AreEqual(1, h.Session.OnLobbyPlayerAddedArgs.Count);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, h.Session.OnLobbyPlayerAddedArgs[0]);
        }

        [Test]
        public void LobbyPlayersRemoved_BatchOfMultiplePlayers_AllForwardedInHookArgsAndLastRemovedIds()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A", "B", "C" });

            h.LobbyTracker.RemoveTrackedPlayers(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.LastRemovedLobbyPlayerIds);
            Assert.AreEqual(1, h.Session.OnLobbyPlayerRemovedArgs.Count);
            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.OnLobbyPlayerRemovedArgs[0]);
            CollectionAssert.AreEqual(new[] { "C" }, h.Session.LobbyPlayerIds);
        }
    }
}
