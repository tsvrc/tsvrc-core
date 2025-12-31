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
    public class TsvrcPlayerListTracker : TsvrcProcess
    {
        [UdonSynced] private string[] _playerIds = new string[0];

        #region VRChat Callbacks
        /// <summary>
        /// Updates local state when synced data is received.
        /// </summary>
        public override void OnDeserialization()
        {
            OnTrackerSynced();
        }

        #endregion

        #region TsvrcProcess Callbacks

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            _playerIds = new string[0];
            RequestSerialization();
        }

        protected override void OnProcessCompleted()
        {
            base.OnProcessCompleted();

            OnTrackerProcessCompleted(_playerIds);
            _playerIds = new string[0];
            RequestSerialization();
        }

        protected override void OnProcessCancelled()
        {
            base.OnProcessCancelled();

            _playerIds = new string[0];
            RequestSerialization();
        }

        /// <summary>
        /// Called when a player leaves the instance.
        /// Only called on the tracker owner when the process is running.
        /// <para>
        /// To override this method, ensure to call base.OnTsPlayerLeft at
        /// the start of your override to maintain correct behavior.
        /// </para>
        /// </summary>
        protected override void OnTsPlayerLeft(VRCPlayerApi player)
        {
            RemoveTrackedPlayer(TsPlayerUtils.GetPlayerID(player));
        }

        #endregion

        #region Public Methods

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
            if (playerIds == null || playerIds.Length == 0)
            {
                Debug.LogWarning("AddTrackedPlayers called with null or empty array");
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AddTrackedPlayersEvent), playerIds);
        }

        /// <summary>
        /// Removes multiple players from the tracked list.
        /// </summary>
        public void RemoveTrackedPlayers(string[] playerIds)
        {
            if (playerIds == null || playerIds.Length == 0 || _playerIds.Length == 0)
            {
                Debug.LogWarning("RemoveTrackedPlayers called with null or empty array");
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RemoveTrackedPlayersEvent), playerIds);
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
        protected bool IsTrackedPlayer(string playerId)
        {
            return TsArray.Contains(_playerIds, playerId);
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when the tracking process is completed.
        /// Only called on the tracker owner.
        /// </summary>
        protected virtual void OnTrackerProcessCompleted(string[] playerIds) { }

        /// <summary>
        /// Called when the tracker's synced data is received via OnDeserialization.
        /// This provides the initial full player list to clients when they join or when the list is updated.
        /// For individual player add/remove events, use OnTrackedPlayerAdded and OnTrackedPlayerRemoved.
        /// </summary>
        protected virtual void OnTrackedPlayersSynced(VRCPlayerApi[] players) { }

        /// <summary>
        /// Called when players are added to the tracked list.
        /// Only called on the tracker owner.
        /// </summary>
        protected virtual void OnTrackedPlayersAdded(VRCPlayerApi[] players) { }

        /// <summary>
        /// Called when players are removed from the tracked list.
        /// Only called on the tracker owner.
        /// </summary>
        protected virtual void OnTrackedPlayersRemoved(VRCPlayerApi[] players) { }

        #endregion

        #region  Network Events

        /// <summary>
        /// Adds players to the tracked list.
        /// This method is network callable and is intended to be called on the owner.
        /// </summary>
        [NetworkCallable]
        public void AddTrackedPlayersEvent(string[] playerIds)
        {
            // Filter out already tracked players
            string[] validPlayerIds = new string[playerIds.Length];
            int validCount = 0;

            for (int i = 0; i < playerIds.Length; i++)
            {
                if (!IsTrackedPlayer(playerIds[i]))
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

            OnTrackedPlayersAdded(TsPlayerUtils.ToPlayerApis(validPlayerIds));
        }

        /// <summary>
        /// Removes players from the tracked list.
        /// This method is network callable and is intended to be called on the owner.
        /// </summary>
        [NetworkCallable]
        public void RemoveTrackedPlayersEvent(string[] playerIds)
        {
            // Filter to only tracked players
            string[] validPlayerIds = new string[playerIds.Length];
            int validCount = 0;

            for (int i = 0; i < playerIds.Length; i++)
            {
                if (IsTrackedPlayer(playerIds[i]))
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

            OnTrackedPlayersRemoved(TsPlayerUtils.ToPlayerApis(validPlayerIds));
        }

        #endregion

        #region Private Methods

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