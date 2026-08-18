using Tsvrc.Core;
using Tsvrc.Tracking;
using Tsvrc.Timing;
using Tsvrc.Utils;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Session
{
    public static class RankedGameSessionState
    {
        public const int Idle = 0;
        public const int Loading = 1;
        public const int InGame = 2;
    }

    /// <summary>A single player's status within a <see cref="RankedGameSession"/>, replacing the need
    /// for consumers to cross-reference <see cref="RankedGameSession.LobbyPlayerIds"/>/
    /// <see cref="RankedGameSession.GamePlayerIds"/>/<see cref="RankedGameSession.CompletedPlayerIds"/> by hand.</summary>
    public static class RankedGamePlayerStatus
    {
        public const int NotInSession = 0;
        public const int InLobby = 1;
        public const int Loading = 2;
        public const int Playing = 3;
        public const int Completed = 4;
    }

    /// <summary>Why a <see cref="RankedGameSession"/> most recently ended or stopped, readable via
    /// <see cref="RankedGameSession.LastEndReason"/>.</summary>
    public static class RankedGameSessionEndReason
    {
        public const int None = 0;
        public const int AllPlayersCompleted = 1;
        public const int AllPlayersLeft = 2;
        public const int TimerExpired = 3;
        public const int ForceStoppedByMaster = 4;
        public const int EmptyLobbyDuringLoading = 5;
    }

    /// <summary>Handles the full lifecycle of a session based multiplayer game: tracks who is in the lobby,
    /// synchronizes game start across all clients, tracks players during the game, and ends the session
    /// when a condition is met. Subscribe to the On... event constants to react to each state change.
    /// Call <see cref="StartLobbyTracking"/> before adding players with <see cref="AddLobbyPlayer"/>.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [TsWorldExtensionPoint("TsRankedGameSession")]
    public class RankedGameSession : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        /// <summary>Emitted when a game start is initiated and the ready check begins. Read <see cref="LobbyPlayerIds"/>.</summary>
        public const string OnSessionLoadingEvent = "OnSessionLoading";
        /// <summary>Emitted when all lobby players are ready and the game is active. Read <see cref="LobbyPlayerIds"/>.</summary>
        public const string OnSessionStartedEvent = "OnSessionStarted";
        /// <summary>Emitted when the session is stopped early by StopSession or by a cancelled ready check.</summary>
        public const string OnSessionStoppedEvent = "OnSessionStopped";
        /// <summary>Emitted when the session ends naturally (timer expired, all active players left, or all players completed).</summary>
        public const string OnSessionEndedEvent = "OnSessionEnded";
        /// <summary>Emitted when a player is added to the lobby. Read <see cref="LastAddedLobbyPlayerIds"/>.</summary>
        public const string OnLobbyPlayerAddedEvent = "OnLobbyPlayerAdded";
        /// <summary>Emitted when a player is removed from the lobby. Read <see cref="LastRemovedLobbyPlayerIds"/>.</summary>
        public const string OnLobbyPlayerRemovedEvent = "OnLobbyPlayerRemoved";
        /// <summary>Emitted when players leave during an active session. Read <see cref="LastRemovedGamePlayerIds"/>.</summary>
        public const string OnGamePlayerRemovedEvent = "OnGamePlayerRemoved";
        /// <summary>Emitted when a player completes the game. Read <see cref="LastCompletedPlayerIds"/>.</summary>
        public const string OnPlayerCompletedEvent = "OnPlayerCompleted";
        /// <summary>Emitted on each timer tick.</summary>
        public const string OnTimerUpdatedEvent = "OnTimerUpdated";

        [WirePool][SerializeField] private PlayerTracker _lobbyTracker;
        [WirePool][SerializeField] private ReadyCheckProcess _readyCheck;
        [WirePool][SerializeField] private PlayerTracker _gameTracker;
        [WirePool][SerializeField] private PlayerTracker _completedTracker;
        [WirePool][SerializeField] private TsvrcTimer _timer;

        [Header("Session")]
        [SerializeField] private bool _masterOnly = true;
        [SerializeField] private bool _endOnTimerComplete = true;
        [SerializeField] private bool _endOnAllGamePlayersLeft = true;
        [SerializeField] private bool _endOnAllPlayersCompleted = true;
        [SerializeField] private bool _stopOnEmptyLobbyDuringLoading = true;
        [SerializeField] private int _timerDurationMs = 0; // 0 = timer will not end the session

        /// <summary>
        /// Current session state, compared against <see cref="RankedGameSessionState"/> constants.
        /// Derived from the sub-trackers' own synced running state, so it reads correctly for any
        /// client at any moment, including one that joined mid-session.
        /// </summary>
        public int CurrentState =>
            _gameTracker.IsProcessRunning() ? RankedGameSessionState.InGame :
            _readyCheck.IsProcessRunning() ? RankedGameSessionState.Loading :
            RankedGameSessionState.Idle;

        public string[] LobbyPlayerIds => _lobbyTracker.LastPlayerIds;
        public string[] GamePlayerIds => _gameTracker.LastPlayerIds;
        public string[] CompletedPlayerIds => _completedTracker.LastPlayerIds;

        /// <summary>Players added in the most recent <see cref="OnLobbyPlayerAddedEvent"/>.</summary>
        public string[] LastAddedLobbyPlayerIds { get; private set; } = new string[0];
        /// <summary>Players removed in the most recent <see cref="OnLobbyPlayerRemovedEvent"/>.</summary>
        public string[] LastRemovedLobbyPlayerIds { get; private set; } = new string[0];
        /// <summary>Players removed in the most recent <see cref="OnGamePlayerRemovedEvent"/>.</summary>
        public string[] LastRemovedGamePlayerIds { get; private set; } = new string[0];
        /// <summary>Players added in the most recent <see cref="OnPlayerCompletedEvent"/>.</summary>
        public string[] LastCompletedPlayerIds { get; private set; } = new string[0];
        /// <summary>
        /// <see cref="GamePlayerIds"/> as it was immediately before the most recent
        /// <see cref="OnSessionEndedEvent"/>/<see cref="OnSessionStoppedEvent"/> (from an InGame
        /// stop) tore the game tracker down. Read this instead of <see cref="GamePlayerIds"/>
        /// from inside those events' handlers - by the time they fire, the live tracker has
        /// already been stopped and cleared.
        /// </summary>
        public string[] LastEndedGamePlayerIds { get; private set; } = new string[0];
        /// <summary>Same snapshot timing as <see cref="LastEndedGamePlayerIds"/>, for <see cref="CompletedPlayerIds"/>.</summary>
        public string[] LastEndedCompletedPlayerIds { get; private set; } = new string[0];
        /// <summary>Why the session most recently ended (<see cref="OnSessionEndedEvent"/>) or stopped
        /// (<see cref="OnSessionStoppedEvent"/>), compared against <see cref="RankedGameSessionEndReason"/>.
        /// Set immediately before the corresponding event fires, same snapshot timing as <see cref="LastEndedGamePlayerIds"/>.</summary>
        public int LastEndReason { get; private set; } = RankedGameSessionEndReason.None;

        private int _pendingEndReason = RankedGameSessionEndReason.None;

        /// <summary>This player's current status, derived from the tracked-player arrays already
        /// correct for any client at any moment - no new synced state.</summary>
        public int GetPlayerStatus(string playerId)
        {
            if (TsArray.Contains(CompletedPlayerIds, playerId)) return RankedGamePlayerStatus.Completed;
            if (TsArray.Contains(GamePlayerIds, playerId)) return RankedGamePlayerStatus.Playing;
            if (CurrentState == RankedGameSessionState.Loading && TsArray.Contains(LobbyPlayerIds, playerId))
                return RankedGamePlayerStatus.Loading;
            if (TsArray.Contains(LobbyPlayerIds, playerId)) return RankedGamePlayerStatus.InLobby;
            return RankedGamePlayerStatus.NotInSession;
        }

        protected override void TsStart()
        {
            base.TsStart();

            if (_lobbyTracker == null)
            {
                LogError("_lobbyTracker is not assigned. Try regenerating TsVRC.");
                return;
            }
            if (_readyCheck == null)
            {
                LogError("_readyCheck is not assigned. Try regenerating TsVRC.");
                return;
            }
            if (_gameTracker == null)
            {
                LogError("_gameTracker is not assigned. Try regenerating TsVRC.");
                return;
            }
            if (_completedTracker == null)
            {
                LogError("_completedTracker is not assigned. Try regenerating TsVRC.");
                return;
            }
            if (_timer == null)
            {
                LogError("_timer is not assigned. Try regenerating TsVRC.");
                return;
            }

            _lobbyTracker.TsSubscribe(this, PlayerTracker.OnTrackingPlayersAddedEvent, nameof(_OnLobbyPlayersAdded));
            _lobbyTracker.TsSubscribe(this, PlayerTracker.OnTrackingPlayersRemovedEvent, nameof(_OnLobbyPlayersRemoved));

            _readyCheck.TsSubscribe(this, ReadyCheckProcess.OnReadyCheckStartedEvent, nameof(_OnReadyCheckStarted));
            _readyCheck.TsSubscribe(this, ReadyCheckProcess.OnReadyCheckCompletedEvent, nameof(_OnReadyCheckCompleted));
            _readyCheck.TsSubscribe(this, ReadyCheckProcess.OnReadyCheckStoppedEvent, nameof(_OnReadyCheckStopped));
            _readyCheck.TsSubscribe(this, PlayerTracker.OnTrackingPlayersRemovedEvent, nameof(_OnReadyCheckPlayersRemoved));

            _gameTracker.TsSubscribe(this, PlayerTracker.OnTrackingPlayersRemovedEvent, nameof(_OnGamePlayersRemoved));

            _completedTracker.TsSubscribe(this, PlayerTracker.OnTrackingPlayersAddedEvent, nameof(_OnPlayersCompleted));

            _timer.TsSubscribe(this, TsvrcTimer.OnTimerUpdatedEvent, nameof(_OnTimerUpdated));
            _timer.TsSubscribe(this, TsvrcTimer.OnTimerCompletedEvent, nameof(_OnTimerCompleted));
        }

        public void StartLobbyTracking() => _lobbyTracker.StartPlayerTracking(new string[0]);
        public void StopLobbyTracking() => _lobbyTracker.StopPlayerTracking();

        /// <summary>Requires <c>_masterOnly</c> to be false or the local player to be TsMaster.</summary>
        public void StartSession()
        {
            if (_masterOnly && !_ts.Instance.IsTsMaster)
            {
                LogWarning("StartSession: ignored, local player is not TsMaster.");
                return;
            }
            if (CurrentState != RankedGameSessionState.Idle)
            {
                LogError("StartSession: session is already running.");
                return;
            }
            // ReadyCheckProcess.CheckAllPlayersReady returns early (never auto-completes)
            // when zero players are tracked, so starting with an empty lobby would leave
            // the session stuck in Loading forever with no player able to ever complete
            // the check - only StopSession could recover it.
            if (LobbyPlayerIds.Length == 0)
            {
                LogError("StartSession: lobby is empty.");
                return;
            }
            _readyCheck.StartReadyCheck(LobbyPlayerIds);
        }

        /// <summary>During the Loading phase this cancels the ready check rather than ending the game directly.</summary>
        public void StopSession() => StopSession(RankedGameSessionEndReason.ForceStoppedByMaster);

        private void StopSession(int reason)
        {
            if (CurrentState == RankedGameSessionState.Idle)
            {
                LogError("StopSession: no session is running.");
                return;
            }
            _pendingEndReason = reason;
            if (CurrentState == RankedGameSessionState.Loading)
            {
                // StopReadyCheck fires _OnReadyCheckStopped which handles state reset.
                _readyCheck.StopReadyCheck();
                return;
            }
            _EndSession(false);
        }

        /// <summary>Each client calls this during the loading phase. When all lobby players have called it, the session transitions to InGame.</summary>
        public void SetReady() => _readyCheck.SetReady();

        /// <summary>Grows the in-progress ready check with a player who wasn't part of the
        /// roster <see cref="StartSession"/> snapshotted - e.g. a game that hands every instance
        /// player the loading data can use this to let one of them join the round for real the
        /// moment they become locally eligible, without waiting for the next round. A no-op
        /// outside the Loading phase, and for a player already tracked (mirrors
        /// <see cref="AddLobbyPlayer"/>'s underlying <c>AddTrackedPlayers</c>, which silently
        /// ignores both).</summary>
        public void AddLoadingParticipant(string playerId)
        {
            if (CurrentState != RankedGameSessionState.Loading) return;
            _readyCheck.AddTrackedPlayers(new[] { playerId });
        }

        /// <summary>Call before <see cref="StartSession"/> to override the inspector-configured duration at runtime.</summary>
        public void SetTimerDuration(int ms) { _timerDurationMs = ms; }

        public int GetRemainingMilliseconds() => _timer.GetRemainingMilliseconds();

        public void AddLobbyPlayer(string playerId)
        {
            if (!_lobbyTracker.IsProcessRunning())
            {
                LogError("AddLobbyPlayer: lobby tracker is not running. Call StartLobbyTracking() first.");
                return;
            }
            _lobbyTracker.AddTrackedPlayers(new[] { playerId });
        }

        public void RemoveLobbyPlayer(string playerId)
        {
            if (!_lobbyTracker.IsProcessRunning())
            {
                LogError("RemoveLobbyPlayer: lobby tracker is not running. Call StartLobbyTracking() first.");
                return;
            }
            _lobbyTracker.RemoveTrackedPlayers(new[] { playerId });
        }

        /// <summary>Call when a player leaves the game early without leaving VRC (respawn, exit trigger, etc.).
        /// Has no effect when the session is not active.</summary>
        public void RemoveGamePlayer(string playerId)
        {
            if (CurrentState != RankedGameSessionState.InGame) return;
            _gameTracker.RemoveTrackedPlayers(new[] { playerId });
        }

        /// <summary>Call from your game-specific win condition. If <c>_endOnAllPlayersCompleted</c> is enabled and
        /// all active players have completed, the session ends naturally.</summary>
        public void AddCompletedPlayer(string playerId)
        {
            if (CurrentState != RankedGameSessionState.InGame)
            {
                LogError("AddCompletedPlayer: session is not in game state.");
                return;
            }
            // Without this check, a caller passing an arbitrary/spoofed playerId not in
            // GamePlayerIds would still inflate _completedTracker's count, letting
            // _OnPlayersCompleted's completed->=game comparison end the session before
            // every real game player has actually completed.
            if (!TsArray.Contains(GamePlayerIds, playerId))
            {
                LogError("AddCompletedPlayer: playerId is not an active game player.");
                return;
            }
            _completedTracker.AddTrackedPlayers(new[] { playerId });
        }

        public void _OnLobbyPlayersAdded()
        {
            LastAddedLobbyPlayerIds = _lobbyTracker.LastAddedPlayerIds;
            OnLobbyPlayerAdded(LastAddedLobbyPlayerIds);
            TsEmit(OnLobbyPlayerAddedEvent);
        }

        public void _OnLobbyPlayersRemoved()
        {
            LastRemovedLobbyPlayerIds = _lobbyTracker.LastRemovedPlayerIds;
            OnLobbyPlayerRemoved(LastRemovedLobbyPlayerIds);
            TsEmit(OnLobbyPlayerRemovedEvent);
        }

        public void _OnReadyCheckStarted()
        {
            OnSessionLoading();
            TsEmit(OnSessionLoadingEvent);
        }

        public void _OnReadyCheckCompleted()
        {
            _gameTracker.StartPlayerTracking(_readyCheck.LastPlayerIds);
            _completedTracker.StartPlayerTracking(new string[0]);
            _timer.StartTimer(_timerDurationMs);
            OnSessionStarted();
            TsEmit(OnSessionStartedEvent);
        }

        public void _OnReadyCheckStopped()
        {
            LastEndReason = _pendingEndReason;
            _pendingEndReason = RankedGameSessionEndReason.None;
            _StopSubProcesses();
            OnSessionStopped();
            TsEmit(OnSessionStoppedEvent);
        }

        // CheckAllPlayersReady (ReadyCheckProcess) never completes on an empty tracked-player
        // list, so a Loading session that loses every remaining player needs an explicit stop
        // here or it hangs forever. Mirrors _endOnAllGamePlayersLeft's InGame equivalent, one
        // phase earlier.
        public void _OnReadyCheckPlayersRemoved()
        {
            if (_stopOnEmptyLobbyDuringLoading && CurrentState == RankedGameSessionState.Loading &&
                _readyCheck.LastPlayerIds.Length == 0)
                StopSession(RankedGameSessionEndReason.EmptyLobbyDuringLoading);
        }

        public void _OnGamePlayersRemoved()
        {
            LastRemovedGamePlayerIds = _gameTracker.LastRemovedPlayerIds;
            OnGamePlayerRemoved(LastRemovedGamePlayerIds);
            TsEmit(OnGamePlayerRemovedEvent);
            if (_endOnAllGamePlayersLeft && _gameTracker.LastPlayerIds.Length == 0)
            {
                _pendingEndReason = RankedGameSessionEndReason.AllPlayersLeft;
                _EndSession(true);
            }
        }

        public void _OnPlayersCompleted()
        {
            LastCompletedPlayerIds = _completedTracker.LastAddedPlayerIds;
            OnPlayerCompleted(LastCompletedPlayerIds);
            TsEmit(OnPlayerCompletedEvent);
            if (_endOnAllPlayersCompleted && _completedTracker.LastPlayerIds.Length >= _gameTracker.LastPlayerIds.Length)
            {
                _pendingEndReason = RankedGameSessionEndReason.AllPlayersCompleted;
                _EndSession(true);
            }
        }

        public void _OnTimerUpdated() => TsEmit(OnTimerUpdatedEvent);

        public void _OnTimerCompleted()
        {
            if (!_endOnTimerComplete) return;
            _pendingEndReason = RankedGameSessionEndReason.TimerExpired;
            _EndSession(true);
        }

        // CurrentState reads Idle once _StopSubProcesses() below stops _gameTracker, so if two
        // end conditions fire at the same time (timer and all players leaving), the second call
        // is rejected by the guard below.
        private void _EndSession(bool natural)
        {
            if (CurrentState != RankedGameSessionState.InGame)
            {
                LogError("_EndSession: session is not in game state.");
                return;
            }
            // Snapshot before _StopSubProcesses() clears the live trackers, so OnSessionEnded/
            // OnSessionStopped subscribers have accurate data to read - GamePlayerIds/
            // CompletedPlayerIds themselves would already read empty by the time those events fire.
            LastEndedGamePlayerIds = GamePlayerIds;
            LastEndedCompletedPlayerIds = CompletedPlayerIds;
            LastEndReason = _pendingEndReason;
            _pendingEndReason = RankedGameSessionEndReason.None;
            _StopSubProcesses();
            if (natural) { OnSessionEnded(); TsEmit(OnSessionEndedEvent); }
            else { OnSessionStopped(); TsEmit(OnSessionStoppedEvent); }
        }

        private void _StopSubProcesses()
        {
            if (_gameTracker.IsProcessRunning()) _gameTracker.StopPlayerTracking();
            if (_completedTracker.IsProcessRunning()) _completedTracker.StopPlayerTracking();
            if (_timer.IsProcessRunning()) _timer.StopTimer();
        }

        // Virtual hooks fire before TsEmit so subclasses have priority over external subscribers.
        protected virtual void OnSessionLoading() { }
        protected virtual void OnSessionStarted() { }
        protected virtual void OnSessionStopped() { }
        protected virtual void OnSessionEnded() { }
        protected virtual void OnLobbyPlayerAdded(string[] addedIds) { }
        protected virtual void OnLobbyPlayerRemoved(string[] removedIds) { }
        protected virtual void OnGamePlayerRemoved(string[] removedIds) { }
        protected virtual void OnPlayerCompleted(string[] completedIds) { }
    }
}
