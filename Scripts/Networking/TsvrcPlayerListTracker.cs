using Tsvrc.Core;
using Tsvrc.List.Utils;
using Tsvrc.TsNetworking.Utils;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.TsNetworking
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcPlayerListTracker : TsvrcBehaviour
    {
        [UdonSynced] private string[] _playerIds = new string[0];
        [UdonSynced] private bool _isTracking = false;

        #region Unity Lifecycle
        #endregion

        #region VRChat Callbacks

#pragma warning disable
        public override void OnPlayerLeft(VRCPlayerApi player)
#pragma warning restore
        {
            if (!_isTracking) return;

            /* If the owner is also the master, UdonSharp auto-transfers ownership before 
             invoking the OnPlayerLeft; otherwise we must reassign it. */
            if (player.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.Master, gameObject);
            }

            if (!IsTrackerOwner()) return;
            RemoveTrackedPlayer(TsPlayerUtils.GetPlayerID(player));
        }

        /// <summary>
        /// Updates local state when synced data is received.
        /// </summary>
        public override void OnDeserialization()
        {
            OnTrackerSynced();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts tracking players with ownership mode enabled.
        /// </summary>
        public void StartTracking()
        {
            if (_isTracking)
            {
                Debug.LogWarning("[TsvrcPlayerListTracker] Tracking is already in progress.");
                return;
            }

            Networking.SetOwner(Networking.LocalPlayer, gameObject);

            StopTracking();
            _isTracking = true;

            RequestSerialization();
        }

        /// <summary>
        /// Sets a new owner for the player list tracker.
        /// </summary>
        public void SetTrackerOwner(VRCPlayerApi newOwner)
        {
            Networking.SetOwner(newOwner, gameObject);
        }

        /// <summary>
        /// Cancels tracking and clears all tracked players.
        /// </summary>
        public void StopTracking()
        {
            if (!IsTrackerOwner()) return;

            _playerIds = new string[0];
            _isTracking = false;
            RequestSerialization();
        }

        /// <summary>
        /// Adds a single player to the tracked list.
        /// </summary>
        public void AddTrackedPlayer(string playerId)
        {
            string[] playerIds = new string[1];
            playerIds[0] = playerId;
            AddTrackedPlayers(playerIds);
        }

        /// <summary>
        /// Removes a single player from the tracked list.
        /// </summary>
        public void RemoveTrackedPlayer(string playerId)
        {
            string[] playerIds = new string[1];
            playerIds[0] = playerId;
            RemoveTrackedPlayers(playerIds);
        }

        /// <summary>
        /// Adds multiple players to the tracked list.
        /// </summary>
        public void AddTrackedPlayers(string[] playerIds)
        {
            if (!IsTrackerOwner()) return;
            if (playerIds == null || playerIds.Length == 0) return;

            // Filter out already tracked players
            string[] validPlayerIds = new string[playerIds.Length];
            int validCount = 0;

            for (int i = 0; i < playerIds.Length; i++)
            {
                if (!ContainsTrackedPlayer(playerIds[i]))
                {
                    validPlayerIds[validCount++] = playerIds[i];
                }
            }

            if (validCount == 0) return;

            // Trim to actual count
            if (validCount < playerIds.Length)
            {
                string[] trimmed = new string[validCount];
                System.Array.Copy(validPlayerIds, trimmed, validCount);
                validPlayerIds = trimmed;
            }

            _playerIds = TsArray.Add(_playerIds, validPlayerIds);
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastPlayersAdded), validPlayerIds);
        }

        /// <summary>
        /// Removes multiple players from the tracked list.
        /// </summary>
        public void RemoveTrackedPlayers(string[] playerIds)
        {
            if (!IsTrackerOwner()) return;
            if (playerIds == null || playerIds.Length == 0 || _playerIds.Length == 0) return;

            // Filter to only tracked players
            string[] validPlayerIds = new string[playerIds.Length];
            int validCount = 0;

            for (int i = 0; i < playerIds.Length; i++)
            {
                if (ContainsTrackedPlayer(playerIds[i]))
                {
                    validPlayerIds[validCount++] = playerIds[i];
                }
            }

            if (validCount == 0) return;

            // Trim to actual count
            if (validCount < playerIds.Length)
            {
                string[] trimmed = new string[validCount];
                System.Array.Copy(validPlayerIds, trimmed, validCount);
                validPlayerIds = trimmed;
            }

            _playerIds = TsArray.Remove(_playerIds, validPlayerIds);
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastPlayersRemoved), validPlayerIds);
        }
        #endregion

        #region Protected Methods

        /// <summary>
        /// Gets the list of currently tracked player IDs.
        /// </summary>
        protected string[] GetTrackedPlayerIds()
        {
            return _playerIds;
        }

        /// <summary>
        /// Checks if a player with the given ID is being tracked.
        /// </summary>
        protected bool ContainsTrackedPlayer(string playerId)
        {
            return TsArray.Contains(_playerIds, playerId);
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when the tracker's synced data is received via OnDeserialization.
        /// This provides the initial full player list to clients when they join or when the list is updated.
        /// For individual player add/remove events, use OnTrackedPlayerAdded and OnTrackedPlayerRemoved.
        /// </summary>
        protected virtual void OnTrackedPlayersSynced(VRCPlayerApi[] players)
        {
            // Override this in child classes to handle initial player list sync
        }

        /// <summary>
        /// Called when players are added to the tracked list.
        /// </summary>
        protected virtual void OnTrackedPlayersAdded(VRCPlayerApi[] players)
        {
            // Override this in child classes to handle player additions
        }

        /// <summary>
        /// Called when players are removed from the tracked list.
        /// </summary>
        protected virtual void OnTrackedPlayersRemoved(VRCPlayerApi[] players)
        {
            // Override this in child classes to handle player removals
        }

        #endregion

        #region Network Events

        /// <summary>
        /// Network callable event to trigger players added callback on all clients.
        /// </summary>
        [NetworkCallable]
        public void BroadcastPlayersAdded(string[] playerIds)
        {
            VRCPlayerApi[] players = TsPlayerUtils.ToPlayerApis(playerIds);
            OnTrackedPlayersAdded(players);
        }

        /// <summary>
        /// Network callable event to trigger players removed callback on all clients.
        /// </summary>
        [NetworkCallable]
        public void BroadcastPlayersRemoved(string[] playerIds)
        {
            VRCPlayerApi[] players = TsPlayerUtils.ToPlayerApis(playerIds);
            OnTrackedPlayersRemoved(players);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Determines if the local player can modify the player list based on ownership.
        /// </summary>
        private bool IsTrackerOwner()
        {
            return Networking.IsOwner(gameObject);
        }

        /// <summary>
        /// Triggers the OnPlayersUpdate callback with current tracked players.
        /// </summary>
        private void OnTrackerSynced()
        {
            VRCPlayerApi[] players = TsPlayerUtils.ToPlayerApis(_playerIds);
            OnTrackedPlayersSynced(players);
        }

        #endregion
    }
}