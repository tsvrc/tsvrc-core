using NUnit.Framework;
using Tsvrc.Session;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // Covers StartSession/StopSession, StartLobbyTracking/StopLobbyTracking,
    // SetReady/AddLoadingParticipant/SetTimerDuration/GetRemainingMilliseconds.
    public class RankedGameSessionStartStopTests : RankedGameSessionTestBase
    {
        [Test]
        public void AllPublicReadState_NeverStarted_ReturnsSaneDefaultsWithoutThrowing()
        {
            // Covers the state every RankedGameSession starts in the instant TsStart
            // wiring completes, before StartLobbyTracking/StartSession are ever
            // called - e.g. a lobby UI reading LobbyPlayerIds/CurrentState before any
            // player has joined.
            var h = CreateWiredSession();

            Assert.DoesNotThrow(() =>
            {
                _ = h.Session.CurrentState;
                _ = h.Session.LobbyPlayerIds;
                _ = h.Session.GamePlayerIds;
                _ = h.Session.CompletedPlayerIds;
                _ = h.Session.LastAddedLobbyPlayerIds;
                _ = h.Session.LastRemovedLobbyPlayerIds;
                _ = h.Session.LastRemovedGamePlayerIds;
                _ = h.Session.LastCompletedPlayerIds;
                _ = h.Session.GetRemainingMilliseconds();
            });

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            CollectionAssert.AreEqual(new string[0], h.Session.LobbyPlayerIds);
            CollectionAssert.AreEqual(new string[0], h.Session.GamePlayerIds);
            CollectionAssert.AreEqual(new string[0], h.Session.CompletedPlayerIds);
            CollectionAssert.AreEqual(new string[0], h.Session.LastAddedLobbyPlayerIds);
            CollectionAssert.AreEqual(new string[0], h.Session.LastRemovedLobbyPlayerIds);
            CollectionAssert.AreEqual(new string[0], h.Session.LastRemovedGamePlayerIds);
            CollectionAssert.AreEqual(new string[0], h.Session.LastCompletedPlayerIds);
            Assert.AreEqual(0, h.Session.GetRemainingMilliseconds());
        }

        [Test]
        public void StartLobbyTracking_StartsLobbyTrackerEmpty()
        {
            var h = CreateWiredSession();

            h.Session.StartLobbyTracking();

            Assert.IsTrue(h.LobbyTracker.IsProcessRunning());
            CollectionAssert.AreEqual(new string[0], h.Session.LobbyPlayerIds);
        }

        [Test]
        public void StopLobbyTracking_StopsLobbyTracker()
        {
            var h = CreateWiredSession();
            h.Session.StartLobbyTracking();

            h.Session.StopLobbyTracking();

            Assert.IsFalse(h.LobbyTracker.IsProcessRunning());
        }

        [Test]
        public void StartSession_MasterOnlyFalse_NonMasterStillStarts()
        {
            var h = CreateWiredSession(); // TestTsRoot: _ts.Instance is null, never dereferenced when _masterOnly is false
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });

            h.Session.StartSession();

            Assert.AreEqual(RankedGameSessionState.Loading, h.Session.CurrentState);
        }

        [Test]
        public void StartSession_MasterOnlyTrueWithRealTsInstance_DefaultIsTsMasterTrue_Starts()
        {
            // A real (non-subclassed) Instance's IsTsMaster defaults to
            // Networking.IsMaster - confirmed here to be true with no real networking
            // session established, not false as might be assumed from "no one is
            // master yet". Only the master-only ALLOWED path is reachable this way;
            // the denied (not master) path needs a real non-master ClientSim local
            // player and lives in the Play Mode suite.
            var root = new InstanceOnlyTsRootDouble { FakeInstance = CreateComponent<Tsvrc.Core.Instance>() };
            var h = CreateWiredSession(root);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });

            h.Session.StartSession();

            Assert.AreEqual(RankedGameSessionState.Loading, h.Session.CurrentState);
        }

        [Test]
        public void StartSession_AlreadyRunning_LogsErrorAndNoOps()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();

            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] StartSession: session is already running.");
            h.Session.StartSession();

            Assert.AreEqual(1, h.Session.OnSessionLoadingCount);
        }

        [Test]
        public void StartSession_EmptyLobby_LogsErrorAndNoOps()
        {
            // Starting a ready check with zero tracked players would otherwise leave the
            // session stuck in Loading forever, since ReadyCheckProcess.CheckAllPlayersReady
            // never auto-completes with no tracked players.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();

            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] StartSession: lobby is empty.");
            h.Session.StartSession();

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(0, h.Session.OnSessionLoadingCount);
            Assert.IsFalse(h.ReadyCheck.IsProcessRunning());
        }

        [Test]
        public void StartSession_NonEmptyLobby_StartsReadyCheckWithLobbySnapshot()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A", "B" });

            h.Session.StartSession();

            Assert.IsTrue(h.ReadyCheck.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "A", "B" }, h.ReadyCheck.LastPlayerIds);
        }

        [Test]
        public void StopSession_Idle_LogsErrorAndNoOps()
        {
            var h = CreateWiredSession();

            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] StopSession: no session is running.");
            h.Session.StopSession();

            Assert.AreEqual(0, h.Session.OnSessionStoppedCount);
        }

        [Test]
        public void StopSession_DuringLoading_RoutesThroughStopReadyCheckBackToIdle()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();

            h.Session.StopSession();

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(1, h.Session.OnSessionStoppedCount);
            Assert.IsFalse(h.ReadyCheck.IsProcessRunning());
        }

        [Test]
        public void StopSession_DuringInGame_EndsSessionNonNaturally()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);

            h.Session.StopSession();

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(1, h.Session.OnSessionStoppedCount);
            Assert.AreEqual(0, h.Session.OnSessionEndedCount);
            Assert.IsFalse(h.GameTracker.IsProcessRunning());
            Assert.IsFalse(h.CompletedTracker.IsProcessRunning());
            Assert.IsFalse(h.Timer.IsProcessRunning());
        }

        [Test]
        public void SetReady_ForwardsToReadyCheck()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "TestOwner#777" });
            h.Session.StartSession();

            h.Session.SetReady();

            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
        }

        [Test]
        public void SetReady_WhileIdle_SilentlyNoOps()
        {
            // ReadyCheckProcess.SetReady()'s own _readyCheckActive guard already makes
            // this safe at the sub-process level; this pins that the no-op holds at
            // the session level too - calling SetReady() before any session has ever
            // started must not throw or change state.
            var h = CreateWiredSession();

            Assert.DoesNotThrow(() => h.Session.SetReady());

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
        }

        [Test]
        public void AddLoadingParticipant_DuringLoading_GrowsTheReadyCheckRoster()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();

            h.Session.AddLoadingParticipant("B");

            CollectionAssert.AreEqual(new[] { "A", "B" }, h.ReadyCheck.LastPlayerIds);
        }

        [Test]
        public void AddLoadingParticipant_NewParticipantBecomingReady_CompletesTheCheckAlongsideTheOriginalRoster()
        {
            // Proves the late addition genuinely participates in completion, not just in the
            // tracked list - the check must not complete until B is ready too, and must
            // complete once both are.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.Session.AddLoadingParticipant("B");
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            Assert.AreEqual(RankedGameSessionState.Loading, h.Session.CurrentState,
                "B hasn't reported ready yet - the round must not start without them.");

            h.ReadyCheck.BroadcastAddReadyPlayer("B");

            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.GamePlayerIds,
                "A late-loading participant who becomes ready must end up in the actual game roster.");
        }

        [Test]
        public void AddLoadingParticipant_WhileIdle_NoOps()
        {
            var h = CreateWiredSession();

            Assert.DoesNotThrow(() => h.Session.AddLoadingParticipant("A"));

            CollectionAssert.AreEqual(new string[0], h.ReadyCheck.LastPlayerIds);
        }

        [Test]
        public void AddLoadingParticipant_WhileInGame_NoOps()
        {
            // A player entering the gaming area after the round has already started can't be
            // caught up - the data transfer that would have given them the maze is long over.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);

            h.Session.AddLoadingParticipant("B");

            CollectionAssert.DoesNotContain(h.Session.GamePlayerIds, "B");
        }

        [Test]
        public void AddLoadingParticipant_AlreadyTrackedPlayer_NoOps()
        {
            // Mirrors AddTrackedPlayers' own silent-ignore behavior for a duplicate id -
            // calling this for a player already part of the original StartSession snapshot
            // must not corrupt the roster or double-count them.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();

            h.Session.AddLoadingParticipant("A");

            CollectionAssert.AreEqual(new[] { "A" }, h.ReadyCheck.LastPlayerIds);
        }

        [Test]
        public void GetRemainingMilliseconds_DuringLoading_BeforeThisRoundsTimerStarts_ReturnsZero()
        {
            // The timer's _durationMs is reset to 0 by its own cleanup after any prior
            // round and only set again once _OnReadyCheckCompleted calls StartTimer -
            // so during Loading (ready check started, not yet all-ready) this must
            // read 0, not stale data from a previous round.
            var h = CreateWiredSession();
            h.Session.SetTimerDuration(5000);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });

            h.Session.StartSession();

            Assert.AreEqual(RankedGameSessionState.Loading, h.Session.CurrentState);
            Assert.AreEqual(0, h.Session.GetRemainingMilliseconds());
        }

        [Test]
        public void SetTimerDuration_SetsFieldUsedByNextStartSession()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);

            h.Session.SetTimerDuration(12345);

            Assert.AreEqual(12345, GetTimerDurationMsField(h.Session));
        }

        [Test]
        public void GetRemainingMilliseconds_ForwardsToTimer()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.SetTimerDuration(1000);
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            Assert.AreEqual(1000, h.Session.GetRemainingMilliseconds());
        }
    }
}
