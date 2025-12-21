using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc
{
    /// <summary>
    /// Error codes for ready check failures.
    /// </summary>
    public enum ReadyCheckError
    {
        None = 0,
        CheckAlreadyInProgress = 1,
        ExceededMaxPlayers = 2,
        OwnerLeftDuringCheck = 3,
        Cancelled = 4
    }

    /// <summary>
    /// Base class for synchronized player ready tracking.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcPlayerReady : UdonSharpBehaviour
    {
        private int _maxPlayers = 255;
        private bool[] _playerChecks = new bool[0];
        protected bool _isCheckInProgress = false;

        private int[] _expectedPlayerIds = new int[0];

        /// <summary>
        /// Monitors the ready check progress and completes it when all expected players are ready.
        /// Only runs on the owner who tracks the ready states.
        /// </summary>
        protected void Update()
        {
            if (!Networking.IsOwner(gameObject)) return;

            if (_isCheckInProgress)
            {
                // Check if any expected players are not ready
                foreach (int playerId in _expectedPlayerIds)
                {
                    if (!GetPlayerReadyStatus(playerId))
                    {
                        return; // At least one player is not ready
                    }
                }

                // All expected players are ready
                _isCheckInProgress = false;

                // Save the completed player IDs before resetting
                int[] completedPlayerIds = _expectedPlayerIds;
                ResetCheckState();

                // Notify all clients that everyone is ready
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(AllPlayersReadyEvent), completedPlayerIds);
            }
        }

        public void SetReady(bool ready = true)
        {
            int playerId = Networking.LocalPlayer.playerId;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(UpdatePlayerCheck), playerId, ready);
        }

        /// <summary>
        /// Updates the ready status for a specific player.
        /// DO NOT CALL DIRECTLY. Use SetReady() instead.
        /// </summary>
        [NetworkCallable]
        public void UpdatePlayerCheck(int playerId, bool ready)
        {
            if (!Networking.IsOwner(gameObject))
            {
                Debug.LogWarning("TsvrcPlayerReady: Only the owner can update player ready status.");
                return;
            }

            _playerChecks[playerId] = ready;
        }

        /// <summary>
        /// Network callable event to notify clients that a ready check has started.
        /// DO NOT CALL DIRECTLY. Use StartReadyCheck() instead.
        /// </summary>
        [NetworkCallable]
        public void CheckStartedEvent(int[] expectedPlayerIds)
        {
            _playerChecks = new bool[_maxPlayers];
            _expectedPlayerIds = expectedPlayerIds;
            _isCheckInProgress = true;
            OnCheckStarted(expectedPlayerIds);
        }

        /// <summary>
        /// Network callable event to notify all clients that all players are ready.
        /// DO NOT CALL DIRECTLY. Called automatically by owner.
        /// </summary>
        [NetworkCallable]
        public void AllPlayersReadyEvent(int[] completedPlayerIds)
        {
            ResetCheckState();
            OnAllPlayersReady(completedPlayerIds);
        }

        /// <summary>
        /// Network callable event to notify all clients that the ready check failed.
        /// DO NOT CALL DIRECTLY. Called automatically when errors occur.
        /// </summary>
        [NetworkCallable]
        public void CheckFailedEvent(int errorCode)
        {
            ReadyCheckError error = (ReadyCheckError)errorCode;

            // Reset state when an active check is aborted
            ResetCheckState();
            OnCheckFailed(error);
        }

        /// <summary>
        /// Network callable event to notify all clients that the ready check was cancelled.
        /// DO NOT CALL DIRECTLY. Use CancelReadyCheck() instead.
        /// </summary>
        [NetworkCallable]
        public void CheckCancelledEvent()
        {
            ResetCheckState();
            OnCheckCancelled();
        }

        /// <summary>
        /// Called when a ready check is started.
        /// Override in subclasses to handle initialization.
        /// </summary>
        /// <param name="expectedPlayerIds">Array of player IDs expected to be ready</param>
        protected virtual void OnCheckStarted(int[] expectedPlayerIds) { }

        /// <summary>
        /// Called when all expected players are ready.
        /// Override in subclasses to handle completion.
        /// </summary>
        /// <param name="playerIds">Array of player IDs who completed the ready check</param>
        protected virtual void OnAllPlayersReady(int[] playerIds) { }

        /// <summary>
        /// Called when the ready check fails.
        /// Override in subclasses to handle errors.
        /// </summary>
        /// <param name="errorCode">The error code indicating why the check failed</param>
        protected virtual void OnCheckFailed(ReadyCheckError errorCode) { }

        /// <summary>
        /// Called when the ready check is cancelled.
        /// Override in subclasses to handle cancellation.
        /// </summary>
        protected virtual void OnCheckCancelled() { }

#pragma warning disable
        public override void OnPlayerLeft(VRCPlayerApi player)
#pragma warning restore
        {
            int leftPlayerId = player.playerId;

            // Check if the owner left during an active check
            if (_isCheckInProgress && Networking.GetOwner(gameObject).playerId == leftPlayerId)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(CheckFailedEvent), (int)ReadyCheckError.OwnerLeftDuringCheck);
                return;
            }

            // Remove the player from expected list
            int[] newExpected = new int[_expectedPlayerIds.Length - 1];
            int index = 0;
            for (int i = 0; i < _expectedPlayerIds.Length; i++)
            {
                if (_expectedPlayerIds[i] != leftPlayerId)
                {
                    newExpected[index++] = _expectedPlayerIds[i];
                }
            }
            _expectedPlayerIds = newExpected;
        }

        /// <summary>
        /// Starts a ready check for the specified players.
        /// </summary>
        /// <param name="targetPlayerIds">Array of player IDs who should be ready</param>
        protected void StartReadyCheck(int[] targetPlayerIds)
        {
            if (targetPlayerIds.Length > _maxPlayers)
            {
                Debug.LogError($"TsvrcPlayerReady: Exceeded max player count of {_maxPlayers}");
                OnCheckFailed(ReadyCheckError.ExceededMaxPlayers);
                return;
            }

            // Check if a ready check is already in progress BEFORE taking ownership
            if (_isCheckInProgress)
            {
                Debug.LogWarning("TsvrcPlayerReady: A ready check is already in progress.");
                OnCheckFailed(ReadyCheckError.CheckAlreadyInProgress);
                return;
            }

            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            _isCheckInProgress = true;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(CheckStartedEvent), targetPlayerIds);
        }

        /// <summary>
        /// Cancels the current ready check if one is in progress.
        /// Only the owner can cancel a check.
        /// </summary>
        public void CancelReadyCheck()
        {
            if (!Networking.IsOwner(gameObject))
            {
                Debug.LogWarning("TsvrcPlayerReady: Only the owner can cancel a ready check.");
                return;
            }

            if (!_isCheckInProgress)
            {
                Debug.LogWarning("TsvrcPlayerReady: No ready check is in progress to cancel.");
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(CheckCancelledEvent));
        }

        /// <summary>
        /// Resets the check state and clears all player ready statuses.
        /// </summary>
        private void ResetCheckState()
        {
            _isCheckInProgress = false;
            _expectedPlayerIds = new int[0];
            _playerChecks = new bool[0];
        }

        private bool GetPlayerReadyStatus(int playerId)
        {
            if (playerId >= 0 && playerId < _maxPlayers)
            {
                return _playerChecks[playerId];
            }
            return false;
        }
    }
}
