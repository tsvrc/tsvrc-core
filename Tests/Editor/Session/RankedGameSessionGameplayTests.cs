using NUnit.Framework;
using Tsvrc.Session;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // Covers RemoveGamePlayer, AddCompletedPlayer (including the membership-guard
    // fix), _OnGamePlayersRemoved, _OnPlayersCompleted, and the _endOnAllGamePlayersLeft/
    // _endOnAllPlayersCompleted flags that decide whether either handler ends the session.
    public class RankedGameSessionGameplayTests : RankedGameSessionTestBase
    {
        private Harness StartInGame(params string[] players)
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(players);
            h.Session.StartSession();
            foreach (var p in players) h.ReadyCheck.BroadcastAddReadyPlayer(p);
            h.Session.CallLog.Clear();
            return h;
        }

        [Test]
        public void RemoveGamePlayer_NotInGame_NoOp()
        {
            var h = CreateWiredSession();

            Assert.DoesNotThrow(() => h.Session.RemoveGamePlayer("A"));
        }

        [Test]
        public void RemoveGamePlayer_InGame_RemovesFromGameTrackerAndFiresHookThenEvent()
        {
            var h = StartInGame("A", "B");
            h.Session.TsSubscribe(h.Session, RankedGameSession.OnGamePlayerRemovedEvent,
                nameof(h.Session._OnGamePlayerRemovedEventReceived));

            h.Session.RemoveGamePlayer("A");

            CollectionAssert.AreEqual(new[] { "B" }, h.Session.GamePlayerIds);
            CollectionAssert.AreEqual(new[] { "A" }, h.Session.LastRemovedGamePlayerIds);
            Assert.AreEqual(1, h.Session.OnGamePlayerRemovedCount);
            CollectionAssert.AreEqual(new[] { "Hook:OnGamePlayerRemoved", "Event:OnGamePlayerRemovedEvent" }, h.Session.CallLog);
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState, "One of two players left - session must still be running.");
        }

        [Test]
        public void GamePlayersRemoved_BatchOfMultiplePlayers_AllForwardedInHookArgs()
        {
            // A mass-disconnect (e.g. an instance restart) can remove several game
            // players simultaneously - driven directly through the game tracker here
            // to prove _OnGamePlayersRemoved forwards the FULL batch, not just a
            // single id.
            var h = StartInGame("A", "B", "C");

            h.GameTracker.RemoveTrackedPlayers(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.LastRemovedGamePlayerIds);
            Assert.AreEqual(1, h.Session.OnGamePlayerRemovedArgs.Count);
            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.OnGamePlayerRemovedArgs[0]);
            CollectionAssert.AreEqual(new[] { "C" }, h.Session.GamePlayerIds);
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState, "C is still in the game.");
        }

        [Test]
        public void PlayersCompleted_BatchOfMultiplePlayers_AllForwardedInHookArgs()
        {
            // AddCompletedPlayer only ever completes one player at a time by its own
            // signature - driven directly through the completed tracker here (bypassing
            // the membership guard, which only lives on AddCompletedPlayer itself) to
            // prove _OnPlayersCompleted forwards the FULL batch when multiple players
            // report completion in the same instant.
            var h = StartInGame("A", "B", "C");

            h.CompletedTracker.AddTrackedPlayers(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.LastCompletedPlayerIds);
            Assert.AreEqual(1, h.Session.OnPlayerCompletedArgs.Count);
            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.OnPlayerCompletedArgs[0]);
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState, "C has not completed yet.");
        }

        [Test]
        public void RemoveGamePlayer_LastPlayerLeaves_EndOnAllGamePlayersLeftTrue_EndsSessionNaturally()
        {
            var h = StartInGame("A");

            h.Session.RemoveGamePlayer("A");

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(1, h.Session.OnSessionEndedCount);
            Assert.AreEqual(0, h.Session.OnSessionStoppedCount);
        }

        [Test]
        public void RemoveGamePlayer_LastPlayerLeaves_EndOnAllGamePlayersLeftFalse_SessionStaysInGame()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            SetEndOnAllGamePlayersLeft(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            h.Session.RemoveGamePlayer("A");

            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
            Assert.AreEqual(0, h.Session.OnSessionEndedCount);
        }

        [Test]
        public void RemoveGamePlayer_PlayerNotInGame_SilentNoOp()
        {
            // Unlike AddCompletedPlayer, no membership guard is needed here:
            // PlayerTracker.RemoveTrackedPlayers already filters its input down to
            // ids that are actually tracked, so an arbitrary/unknown id is already a
            // safe no-op with no observable side effect.
            var h = StartInGame("A");

            Assert.DoesNotThrow(() => h.Session.RemoveGamePlayer("NeverJoined"));

            CollectionAssert.AreEqual(new[] { "A" }, h.Session.GamePlayerIds);
            Assert.AreEqual(0, h.Session.OnGamePlayerRemovedCount);
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
        }

        [Test]
        public void AddCompletedPlayer_NotInGame_LogsErrorAndNoOps()
        {
            var h = CreateWiredSession();

            LogAssert.Expect(LogType.Error, "[TsVRC] RankedGameSession.AddCompletedPlayer: session is not in game state.");
            h.Session.AddCompletedPlayer("A");
        }

        [Test]
        public void AddCompletedPlayer_NotAnActiveGamePlayer_LogsErrorAndNoOps()
        {
            // Regression test for the fix: AddCompletedPlayer used to accept any
            // playerId with no membership check, so a spoofed/arbitrary id could
            // inflate _completedTracker's count and end the session before every
            // real game player had actually completed.
            var h = StartInGame("A", "B");

            LogAssert.Expect(LogType.Error, "[TsVRC] RankedGameSession.AddCompletedPlayer: playerId is not an active game player.");
            h.Session.AddCompletedPlayer("SpoofedPlayer");

            CollectionAssert.AreEqual(new string[0], h.Session.CompletedPlayerIds);
            Assert.AreEqual(0, h.Session.OnPlayerCompletedCount);
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
        }

        [Test]
        public void AddCompletedPlayer_ActiveGamePlayer_AddsAndFiresHookThenEvent()
        {
            var h = StartInGame("A", "B");
            h.Session.TsSubscribe(h.Session, RankedGameSession.OnPlayerCompletedEvent,
                nameof(h.Session._OnPlayerCompletedEventReceived));

            h.Session.AddCompletedPlayer("A");

            CollectionAssert.AreEqual(new[] { "A" }, h.Session.CompletedPlayerIds);
            CollectionAssert.AreEqual(new[] { "A" }, h.Session.LastCompletedPlayerIds);
            Assert.AreEqual(1, h.Session.OnPlayerCompletedCount);
            CollectionAssert.AreEqual(new[] { "Hook:OnPlayerCompleted", "Event:OnPlayerCompletedEvent" }, h.Session.CallLog);
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState, "Only one of two players completed.");
        }

        [Test]
        public void AddCompletedPlayer_AllPlayersCompleted_EndOnAllPlayersCompletedTrue_EndsSessionNaturally()
        {
            var h = StartInGame("A", "B");

            h.Session.AddCompletedPlayer("A");
            h.Session.AddCompletedPlayer("B");

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(1, h.Session.OnSessionEndedCount);
        }

        [Test]
        public void AddCompletedPlayer_AllPlayersCompleted_EndOnAllPlayersCompletedFalse_SessionStaysInGame()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            SetEndOnAllPlayersCompleted(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            h.Session.AddCompletedPlayer("A");

            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
            Assert.AreEqual(0, h.Session.OnSessionEndedCount);
        }

        [Test]
        public void AddCompletedPlayer_DuplicateCallForSamePlayer_DoesNotDoubleCount()
        {
            // PlayerTracker.AddTrackedPlayers/BroadcastAddTrackedPlayers already filters
            // out already-tracked ids, so a repeated completion call for the same
            // player must not push the completed count past the real game player count.
            var h = StartInGame("A", "B");

            h.Session.AddCompletedPlayer("A");
            h.Session.AddCompletedPlayer("A");

            CollectionAssert.AreEqual(new[] { "A" }, h.Session.CompletedPlayerIds);
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
        }

        [Test]
        public void AllEndFlagsFalse_SessionNeverEndsFromAnyNaturalCondition()
        {
            // Confirms StopSession is genuinely the only way out when a game wants
            // full manual control over ending: all three end-condition flags false
            // together, each condition triggered in turn, none of them ending the
            // session.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            SetEndOnAllGamePlayersLeft(h.Session, false);
            SetEndOnAllPlayersCompleted(h.Session, false);
            SetEndOnTimerComplete(h.Session, false);
            h.Session.SetTimerDuration(1000);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            h.Session.AddCompletedPlayer("A"); // would end the session if the flag were true
            h.Session.RemoveGamePlayer("A"); // ditto - last player leaving
            int start = PrivateFieldAccess.GetField<int>(h.Timer, "_startServerTimeMs");
            PrivateFieldAccess.SetField(h.Timer, "_startServerTimeMs", start - 1000);
            ForceNextTickDueNow(h.Timer);
            h.Timer._TickProcessUpdate(); // ditto - timer completing

            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
            Assert.AreEqual(0, h.Session.OnSessionEndedCount);

            h.Session.StopSession();

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(1, h.Session.OnSessionStoppedCount);
        }

        [Test]
        public void OpenEndedTimer_EndOnTimerCompleteTrue_NeverEndsFromTheTimer()
        {
            // _timerDurationMs's own doc comment: "0 = timer will not end the
            // session." An open-ended timer never calls CompleteProcess at all
            // (TsvrcTimer.OnProcessUpdate only completes when _durationMs > 0), so
            // _OnTimerCompleted is never invoked regardless of _endOnTimerComplete -
            // the flag being true (the default) is irrelevant when duration is 0.
            var h = StartInGame("A"); // default SetTimerDuration is never called - duration stays 0

            PrivateFieldAccess.InvokeInstance(h.Timer, "OnProcessUpdate");
            PrivateFieldAccess.InvokeInstance(h.Timer, "OnProcessUpdate");

            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
            Assert.AreEqual(0, h.Session.OnSessionEndedCount);
            Assert.IsTrue(h.Timer.IsProcessRunning());
        }

        [Test]
        public void PlayerCompletesThenLeaves_RemainingPlayerCompleting_StillEndsSessionCorrectly()
        {
            // Realistic scenario: a player finishes their objective, then leaves
            // VRChat entirely (RemoveGamePlayer) before the round itself ends. Their
            // completed-tracker membership is untouched by leaving the game tracker,
            // so the remaining player's own completion must still correctly trigger
            // the all-completed end condition against the now-smaller game roster.
            var h = StartInGame("A", "B");
            h.Session.AddCompletedPlayer("A");
            h.Session.RemoveGamePlayer("A");
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState, "B has not completed yet.");

            h.Session.AddCompletedPlayer("B");

            // CompletedPlayerIds itself is not checked here: _EndSession's cleanup
            // stops _completedTracker, which resets its own LastPlayerIds to empty -
            // the meaningful outcome is that the session actually ended, below.
            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(1, h.Session.OnSessionEndedCount);
            CollectionAssert.AreEqual(new[] { "B" }, h.Session.LastCompletedPlayerIds,
                "LastCompletedPlayerIds reflects only the players added in the final AddCompletedPlayer call, not the full roster.");
        }
    }
}
