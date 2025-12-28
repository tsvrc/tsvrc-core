using Tsvrc.List.Utils;
using Tsvrc.TsNetworking.Utils;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.TsNetworking
{
    /// <summary>
    /// Tracks player readiness.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcPlayerReadyChecker : TsvrcPlayerListTracker
    {
        [UdonSynced] private string[] _readyPlayerIds = new string[0];
        [UdonSynced] protected bool _isCheckInProgress = false;

        #region Unity Lifecycle

        private void Update()
        {
            if (!IsCurrentOwner()) return;
            if (!_isCheckInProgress) return;

            string[] trackedPlayerIds = GetTrackedPlayerIds();

            foreach (string playerId in trackedPlayerIds)
            {
                if (!IsPlayerReady(playerId))
                {
                    return; // At least one player is not ready
                }
            }

            CompleteReadyCheck(trackedPlayerIds);
        }

        #endregion

        #region Tsvrc Callbacks

        protected override void OnTrackedPlayersRemoved(VRCPlayerApi[] players)
        {
            if (!IsCurrentOwner()) return;

            foreach (VRCPlayerApi player in players)
            {
                string playerId = TsPlayerUtils.GetPlayerID(player);
                if (IsPlayerReady(playerId))
                {
                    RemoveReadyPlayer(playerId);
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Set the local player's ready status.
        /// </summary>
        public void SetReady(bool ready = true)
        {
            if (!_isCheckInProgress) return;

            string playerId = TsPlayerUtils.GetPlayerID(Networking.LocalPlayer);
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(UpdatePlayerCheck), playerId, ready);
        }

        /// <summary>
        /// Stops the current ready check.
        /// </summary>
        public void StopReadyCheck()
        {
            if (!IsCurrentOwner()) return;
            if (!_isCheckInProgress) return;

            ResetReadyState();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastCheckCancelled));
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Starts a ready check for the specified players.
        /// </summary>
        protected void StartReadyCheck(string[] playerIds)
        {
            if (_isCheckInProgress)
            {
                Debug.LogWarning("TsvrcPlayerReady: A ready check is already in progress.");
                return;
            }

            if (!IsCurrentOwner())
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            ResetReadyState();
            StartTracking();
            AddTrackedPlayers(playerIds);
            _isCheckInProgress = true;
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastCheckStarted), playerIds);
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when a ready check is started.
        /// Override in subclasses to handle initialization.
        /// </summary>
        /// <param name="expectedPlayerIds">Array of player IDs expected to be ready</param>
        protected virtual void OnReadyCheckStarted(string[] expectedPlayerIds) { }

        /// <summary>
        /// Called when all expected players are ready.
        /// Override in subclasses to handle completion.
        /// </summary>
        /// <param name="playerIds">Array of player IDs who completed the ready check</param>
        protected virtual void OnAllPlayersReady(string[] playerIds) { }

        /// <summary>
        /// Called when the ready check is cancelled.
        /// Override in subclasses to handle cancellation.
        /// </summary>
        protected virtual void OnReadyCheckCancelled() { }

        #endregion

        #region Network Events

        /// <summary>
        /// Updates the ready status for a specific player.
        /// This is an internal method. Use SetReady() instead to set the player ready.
        /// </summary>
        [NetworkCallable]
        public void UpdatePlayerCheck(string playerId, bool ready)
        {
            if (!IsCurrentOwner()) return;

            if (ready)
            {
                AddReadyPlayer(playerId);
            }
            else
            {
                RemoveReadyPlayer(playerId);
            }
        }

        /// <summary>
        /// Network callable event to notify clients that a ready check has started.
        /// </summary>
        [NetworkCallable]
        public void BroadcastCheckStarted(string[] expectedPlayerIds)
        {
            OnReadyCheckStarted(expectedPlayerIds);
        }

        /// <summary>
        /// Network callable event to notify all clients that all players are ready.
        /// </summary>
        [NetworkCallable]
        public void BroadcastAllPlayersReady(string[] completedPlayerIds)
        {
            OnAllPlayersReady(completedPlayerIds);
        }

        /// <summary>
        /// Network callable event to notify all clients that the ready check was cancelled.
        /// </summary>
        [NetworkCallable]
        public void BroadcastCheckCancelled()
        {
            OnReadyCheckCancelled();
        }

        #endregion

        #region Private Methods

        private bool IsCurrentOwner()
        {
            return Networking.IsOwner(gameObject);
        }

        /// <summary>
        /// Resets the ready check state and clears all tracking.
        /// </summary>
        private void ResetReadyState()
        {
            StopTracking();
            _readyPlayerIds = new string[0];
            _isCheckInProgress = false;
            RequestSerialization();
        }

        /// <summary>
        /// Completes the ready check when all players are ready.
        /// Resets tracker and ready lists, then broadcasts to all clients.
        /// </summary>
        private void CompleteReadyCheck(string[] completedPlayerIds)
        {
            if (!IsCurrentOwner()) return;

            ResetReadyState();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastAllPlayersReady), completedPlayerIds);
        }

        /// <summary>
        /// Adds a player to the ready list.
        /// This is an internal method. Use SetReady() instead to set the player ready.
        /// </summary>
        private void AddReadyPlayer(string playerId)
        {
            if (IsPlayerReady(playerId)) return;

            string[] playerIds = new string[1];
            playerIds[0] = playerId;
            _readyPlayerIds = TsArray.Add(_readyPlayerIds, playerIds);
            RequestSerialization();
        }

        /// <summary>
        /// Removes a player from the ready list.
        /// This is an internal method; use SetReady(false) to mark a player as not ready.
        /// </summary>
        private void RemoveReadyPlayer(string playerId)
        {
            if (!IsPlayerReady(playerId)) return;

            string[] playerIds = new string[1];
            playerIds[0] = playerId;
            _readyPlayerIds = TsArray.Remove(_readyPlayerIds, playerIds);
            RequestSerialization();
        }

        /// <summary>
        /// Gets the ready status of a specific player by ID.
        /// </summary>
        private bool IsPlayerReady(string playerId)
        {
            return TsArray.Contains(_readyPlayerIds, playerId);
        }

        #endregion
    }
}
