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

    /// <summary>Handles the full lifecycle of a session based multiplayer game: tracks who is in the lobby,
    /// synchronizes game start across all clients, tracks players during the game, and ends the session
    /// when a condition is met. Subscribe to the On... event constants to react to each state change.
    /// Call <see cref="StartLobbyTracking"/> before adding players with <see cref="AddLobbyPlayer"/>.</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RankedGameSession : TsvrcBehaviour
    {
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
        [SerializeField] private int _timerDurationMs = 0; // 0 = timer will not end the session

        /// <summary>Current session state. Compare against <see cref="RankedGameSessionState"/> constants.</summary>
        public int CurrentState { get; private set; } = RankedGameSessionState.Idle;

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

        protected override void TsStart()
        {
            base.TsStart();

            if (_lobbyTracker == null)
            {
                Debug.LogError("[TsVRC] RankedGameSession._lobbyTracker is not assigned. Try regenerating TsVRC.", this);
                return;
            }
            if (_readyCheck == null)
            {
                Debug.LogError("[TsVRC] RankedGameSession._readyCheck is not assigned. Try regenerating TsVRC.", this);
                return;
            }
            if (_gameTracker == null)
            {
                Debug.LogError("[TsVRC] RankedGameSession._gameTracker is not assigned. Try regenerating TsVRC.", this);
                return;
            }
            if (_completedTracker == null)
            {
                Debug.LogError("[TsVRC] RankedGameSession._completedTracker is not assigned. Try regenerating TsVRC.", this);
                return;
            }
            if (_timer == null)
            {
                Debug.LogError("[TsVRC] RankedGameSession._timer is not assigned. Try regenerating TsVRC.", this);
                return;
            }

            _lobbyTracker.TsSubscribe(this, PlayerTracker.OnTrackingPlayersAddedEvent, nameof(_OnLobbyPlayersAdded));
            _lobbyTracker.TsSubscribe(this, PlayerTracker.OnTrackingPlayersRemovedEvent, nameof(_OnLobbyPlayersRemoved));

            _readyCheck.TsSubscribe(this, ReadyCheckProcess.OnReadyCheckStartedEvent, nameof(_OnReadyCheckStarted));
            _readyCheck.TsSubscribe(this, ReadyCheckProcess.OnReadyCheckCompletedEvent, nameof(_OnReadyCheckCompleted));
            _readyCheck.TsSubscribe(this, ReadyCheckProcess.OnReadyCheckStoppedEvent, nameof(_OnReadyCheckStopped));

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
                Debug.LogWarning("[TsVRC] RankedGameSession.StartSession: ignored, local player is not TsMaster.", this);
                return;
            }
            if (CurrentState != RankedGameSessionState.Idle)
            {
                Debug.LogError("[TsVRC] RankedGameSession.StartSession: session is already running.", this);
                return;
            }
            // ReadyCheckProcess.CheckAllPlayersReady returns early (never auto-completes)
            // when zero players are tracked, so starting with an empty lobby would leave
            // the session stuck in Loading forever with no player able to ever complete
            // the check - only StopSession could recover it.
            if (LobbyPlayerIds.Length == 0)
            {
                Debug.LogError("[TsVRC] RankedGameSession.StartSession: lobby is empty.", this);
                return;
            }
            _readyCheck.StartReadyCheck(LobbyPlayerIds);
        }

        /// <summary>During the Loading phase this cancels the ready check rather than ending the game directly.</summary>
        public void StopSession()
        {
            if (CurrentState == RankedGameSessionState.Idle)
            {
                Debug.LogError("[TsVRC] RankedGameSession.StopSession: no session is running.", this);
                return;
            }
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

        /// <summary>Call before <see cref="StartSession"/> to override the inspector-configured duration at runtime.</summary>
        public void SetTimerDuration(int ms) { _timerDurationMs = ms; }

        public int GetRemainingMilliseconds() => _timer.GetRemainingMilliseconds();

        public void AddLobbyPlayer(string playerId)
        {
            if (!_lobbyTracker.IsProcessRunning())
            {
                Debug.LogError("[TsVRC] RankedGameSession.AddLobbyPlayer: lobby tracker is not running. Call StartLobbyTracking() first.", this);
                return;
            }
            _lobbyTracker.AddTrackedPlayers(new[] { playerId });
        }

        public void RemoveLobbyPlayer(string playerId)
        {
            if (!_lobbyTracker.IsProcessRunning())
            {
                Debug.LogError("[TsVRC] RankedGameSession.RemoveLobbyPlayer: lobby tracker is not running. Call StartLobbyTracking() first.", this);
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
                Debug.LogError("[TsVRC] RankedGameSession.AddCompletedPlayer: session is not in game state.", this);
                return;
            }
            // Without this check, a caller passing an arbitrary/spoofed playerId not in
            // GamePlayerIds would still inflate _completedTracker's count, letting
            // _OnPlayersCompleted's completed->=game comparison end the session before
            // every real game player has actually completed.
            if (!TsArray.Contains(GamePlayerIds, playerId))
            {
                Debug.LogError("[TsVRC] RankedGameSession.AddCompletedPlayer: playerId is not an active game player.", this);
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
            CurrentState = RankedGameSessionState.Loading;
            OnSessionLoading();
            TsEmit(OnSessionLoadingEvent);
        }

        public void _OnReadyCheckCompleted()
        {
            CurrentState = RankedGameSessionState.InGame;
            _gameTracker.StartPlayerTracking(_readyCheck.LastPlayerIds);
            _completedTracker.StartPlayerTracking(new string[0]);
            _timer.StartTimer(_timerDurationMs);
            OnSessionStarted();
            TsEmit(OnSessionStartedEvent);
        }

        public void _OnReadyCheckStopped()
        {
            CurrentState = RankedGameSessionState.Idle;
            _StopSubProcesses();
            OnSessionStopped();
            TsEmit(OnSessionStoppedEvent);
        }

        public void _OnGamePlayersRemoved()
        {
            LastRemovedGamePlayerIds = _gameTracker.LastRemovedPlayerIds;
            OnGamePlayerRemoved(LastRemovedGamePlayerIds);
            TsEmit(OnGamePlayerRemovedEvent);
            if (_endOnAllGamePlayersLeft && _gameTracker.LastPlayerIds.Length == 0)
                _EndSession(true);
        }

        public void _OnPlayersCompleted()
        {
            LastCompletedPlayerIds = _completedTracker.LastAddedPlayerIds;
            OnPlayerCompleted(LastCompletedPlayerIds);
            TsEmit(OnPlayerCompletedEvent);
            if (_endOnAllPlayersCompleted && _completedTracker.LastPlayerIds.Length >= _gameTracker.LastPlayerIds.Length)
                _EndSession(true);
        }

        public void _OnTimerUpdated() => TsEmit(OnTimerUpdatedEvent);
        public void _OnTimerCompleted() { if (_endOnTimerComplete) _EndSession(true); }

        // State is set to Idle before stopping child processes so if two end conditions fire at
        // the same time (timer and all players leaving) the second call is rejected by the CurrentState guard.
        private void _EndSession(bool natural)
        {
            if (CurrentState != RankedGameSessionState.InGame)
            {
                Debug.LogError("[TsVRC] RankedGameSession._EndSession: session is not in game state.", this);
                return;
            }
            CurrentState = RankedGameSessionState.Idle;
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
