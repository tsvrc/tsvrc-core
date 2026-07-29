using NUnit.Framework;
using Tsvrc.Session;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Covers the state machine's internal transitions (_OnReadyCheckStarted/Completed/
    // Stopped, _OnTimerUpdated/_OnTimerCompleted), the _endOnTimerComplete flag,
    // _EndSession's reentrancy guard, _StopSubProcesses idempotency, event ordering,
    // and one full end-to-end lifecycle.
    public class RankedGameSessionLifecycleTests : RankedGameSessionTestBase
    {
        [Test]
        public void OnReadyCheckStarted_TransitionsToLoadingAndFiresHookThenEvent()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.CallLog.Clear();
            h.Session.TsSubscribe(h.Session, RankedGameSession.OnSessionLoadingEvent, nameof(h.Session._OnSessionLoadingEventReceived));

            h.Session.StartSession();

            Assert.AreEqual(RankedGameSessionState.Loading, h.Session.CurrentState);
            CollectionAssert.AreEqual(new[] { "Hook:OnSessionLoading", "Event:OnSessionLoadingEvent" }, h.Session.CallLog);
        }

        [Test]
        public void OnReadyCheckCompleted_TransitionsToInGame_StartsGameCompletedTrackersAndTimer()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.SetTimerDuration(5000);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A", "B" });
            h.Session.StartSession();
            h.Session.TsSubscribe(h.Session, RankedGameSession.OnSessionStartedEvent, nameof(h.Session._OnSessionStartedEventReceived));
            h.Session.CallLog.Clear();

            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            h.ReadyCheck.BroadcastAddReadyPlayer("B");

            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
            CollectionAssert.AreEqual(new[] { "A", "B" }, h.Session.GamePlayerIds);
            CollectionAssert.AreEqual(new string[0], h.Session.CompletedPlayerIds);
            Assert.IsTrue(h.GameTracker.IsProcessRunning());
            Assert.IsTrue(h.CompletedTracker.IsProcessRunning());
            Assert.IsTrue(h.Timer.IsProcessRunning());
            Assert.AreEqual(5000, h.Timer.DurationMs);
            CollectionAssert.AreEqual(new[] { "Hook:OnSessionStarted", "Event:OnSessionStartedEvent" }, h.Session.CallLog);
        }

        [Test]
        public void OnReadyCheckStopped_DuringLoading_TransitionsToIdleAndStopsSubProcesses()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.Session.TsSubscribe(h.Session, RankedGameSession.OnSessionStoppedEvent, nameof(h.Session._OnSessionStoppedEventReceived));
            h.Session.CallLog.Clear();

            h.ReadyCheck.StopReadyCheck();

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            CollectionAssert.AreEqual(new[] { "Hook:OnSessionStopped", "Event:OnSessionStoppedEvent" }, h.Session.CallLog);
        }

        [Test]
        public void OnTimerUpdated_ForwardsEvent()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.SetTimerDuration(1000);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.Session.TsSubscribe(h.Session, RankedGameSession.OnTimerUpdatedEvent, nameof(h.Session._OnTimerUpdatedEventReceived));
            h.Session.CallLog.Clear();

            h.ReadyCheck.BroadcastAddReadyPlayer("A"); // starts the timer, which emits OnTimerUpdated once on start

            // Contains rather than an exact sequence match: the same ready-check
            // completion also fires the session's own OnSessionStarted hook (logged
            // unconditionally by the test double regardless of subscription) - event
            // ordering between different event types is already covered elsewhere.
            CollectionAssert.Contains(h.Session.CallLog, "Event:OnTimerUpdatedEvent");
        }

        [Test]
        public void OnTimerCompleted_EndOnTimerCompleteTrue_EndsSessionNaturally()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.SetTimerDuration(1000);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            int start = PrivateFieldAccess.GetField<int>(h.Timer, "_startServerTimeMs");
            PrivateFieldAccess.SetField(h.Timer, "_startServerTimeMs", start - 1000);

            ForceNextTickDueNow(h.Timer);
            h.Timer._TickProcessUpdate();

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(1, h.Session.OnSessionEndedCount);
        }

        [Test]
        public void OnTimerCompleted_EndOnTimerCompleteFalse_SessionStaysInGame()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            SetEndOnTimerComplete(h.Session, false);
            h.Session.SetTimerDuration(1000);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            int start = PrivateFieldAccess.GetField<int>(h.Timer, "_startServerTimeMs");
            PrivateFieldAccess.SetField(h.Timer, "_startServerTimeMs", start - 1000);

            ForceNextTickDueNow(h.Timer);
            h.Timer._TickProcessUpdate();

            Assert.AreEqual(RankedGameSessionState.InGame, h.Session.CurrentState);
            Assert.AreEqual(0, h.Session.OnSessionEndedCount);
            Assert.IsFalse(h.Timer.IsProcessRunning(), "The timer itself still completed - only the session's reaction is suppressed.");
        }

        [Test]
        public void EndSession_TimerCompleteThenAllPlayersLeave_SecondEndConditionRejectedByStateGuard()
        {
            // Pins the source comment's own documented reentrancy guarantee: CurrentState
            // flips to Idle before _StopSubProcesses runs, so if two end conditions were
            // to fire in the same synchronous chain, the second _EndSession call is
            // rejected rather than double-firing OnSessionEnded/double-stopping already-
            // stopped sub-processes.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.SetTimerDuration(1000);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            int start = PrivateFieldAccess.GetField<int>(h.Timer, "_startServerTimeMs");
            PrivateFieldAccess.SetField(h.Timer, "_startServerTimeMs", start - 1000);
            ForceNextTickDueNow(h.Timer);

            h.Timer._TickProcessUpdate(); // ends the session via the timer

            Assert.AreEqual(1, h.Session.OnSessionEndedCount);

            // A late-arriving "all players left" notification for the same now-idle
            // session must not fire a second OnSessionEnded. _EndSession's own guard
            // rejects it, logging the same error every other rejected call site does.
            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] _EndSession: session is not in game state.");
            h.Session._OnGamePlayersRemoved();

            Assert.AreEqual(1, h.Session.OnSessionEndedCount);
        }

        [Test]
        public void FullLifecycle_LoadingToInGameToEnded_ThenRestarts_DoesNotThrowAndEndsClean()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);

            Assert.DoesNotThrow(() =>
            {
                h.Session.StartLobbyTracking();
                h.LobbyTracker.AddTrackedPlayers(new[] { "A", "B" });
                h.Session.StartSession();
                h.ReadyCheck.BroadcastAddReadyPlayer("A");
                h.ReadyCheck.BroadcastAddReadyPlayer("B");
                h.Session.AddCompletedPlayer("A");
                h.Session.AddCompletedPlayer("B"); // all completed -> ends naturally

                // Every sub-process's own cleanup clears its synced owner fields on a
                // natural stop (Process.InternalCleanup, shared by every process
                // in this library), so a genuine restart needs re-seeding ownership
                // for every one of them, exactly like starting fresh would.
                SeedAsOwner(h.ReadyCheck);
                SeedAsOwner(h.GameTracker);
                SeedAsOwner(h.CompletedTracker);
                SeedAsOwner(h.Timer);
                h.Session.StartSession(); // restart with the same lobby snapshot
                h.ReadyCheck.BroadcastAddReadyPlayer("A");
                h.ReadyCheck.BroadcastAddReadyPlayer("B");
                h.Session.RemoveGamePlayer("A");
                h.Session.RemoveGamePlayer("B"); // all left -> ends naturally
            });

            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
            Assert.AreEqual(2, h.Session.OnSessionEndedCount);
            Assert.AreEqual(2, h.Session.OnSessionStartedCount);
            Assert.AreEqual(0, h.Session.OnSessionStoppedCount);
        }

        [Test]
        public void ReentrantRestartFromOnSessionEnded_NextRoundStartsCleanlyWithoutThrowing()
        {
            // OnSessionEnded fires before CurrentState's caller-visible effects are
            // otherwise observed elsewhere, so a subscriber reacting to it by starting
            // the next round immediately (an automatic "queue the next match" flow) is
            // a realistic scenario this class's design supports - CurrentState is
            // already Idle and every sub-process already stopped by the time the hook
            // runs, exactly like a normal StartSession call would expect.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");

            h.Session.OnSessionEndedAction = () =>
            {
                SeedAsOwner(h.ReadyCheck);
                h.Session.StartSession();
            };

            Assert.DoesNotThrow(() => h.Session.AddCompletedPlayer("A"));

            Assert.AreEqual(RankedGameSessionState.Loading, h.Session.CurrentState,
                "The reentrant restart's own StartSession() call must have taken effect.");
            Assert.AreEqual(1, h.Session.OnSessionEndedCount);
            Assert.AreEqual(2, h.Session.OnSessionLoadingCount, "Once for the original start, once for the reentrant restart.");
        }

        [Test]
        public void EventOrdering_SessionEnded_HookFiresBeforeEvent()
        {
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            h.Session.CallLog.Clear();
            h.Session.TsSubscribe(h.Session, RankedGameSession.OnSessionEndedEvent, nameof(h.Session._OnSessionEndedEventReceived));

            h.Session.AddCompletedPlayer("A");

            // Only subscribed to OnSessionEndedEvent above - OnPlayerCompleted's own
            // hook still logs unconditionally (every hook does, regardless of
            // subscription), but its event line only appears for events actually
            // subscribed to.
            CollectionAssert.AreEqual(new[] { "Hook:OnPlayerCompleted", "Hook:OnSessionEnded", "Event:OnSessionEndedEvent" }, h.Session.CallLog);
        }

        [Test]
        public void EventOrdering_SessionStoppedDuringInGame_HookFiresBeforeEvent()
        {
            // Companion to OnReadyCheckStopped_DuringLoading_TransitionsToIdleAndStopsSubProcesses
            // above, which only covers the Loading-phase stop path (_OnReadyCheckStopped).
            // StopSession during InGame routes through _EndSession(false) instead - a
            // separate code path to the same OnSessionStoppedEvent, needing its own
            // ordering test.
            var h = CreateWiredSession();
            SetMasterOnly(h.Session, false);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            h.Session.StartSession();
            h.ReadyCheck.BroadcastAddReadyPlayer("A");
            h.Session.CallLog.Clear();
            h.Session.TsSubscribe(h.Session, RankedGameSession.OnSessionStoppedEvent, nameof(h.Session._OnSessionStoppedEventReceived));

            h.Session.StopSession();

            CollectionAssert.AreEqual(new[] { "Hook:OnSessionStopped", "Event:OnSessionStoppedEvent" }, h.Session.CallLog);
        }
    }
}
