using Tsvrc.Core;
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
        [UdonSynced] private string[] playerNames = new string[0];
        [UdonSynced] private bool isTracking = false;

        #region Unity Lifecycle
        #endregion

        #region VRChat Callbacks

#pragma warning disable
        public override void OnPlayerLeft(VRCPlayerApi player)
#pragma warning restore
        {
            if (!IsTrackerOwner()) return;
            RemoveTrackedPlayer(player);
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
            if (isTracking)
            {
                Debug.LogWarning("[TsvrcPlayerListTracker] Tracking is already in progress.");
                return;
            }

            Networking.SetOwner(Networking.LocalPlayer, gameObject);

            isTracking = true;
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
        public void CancelTracking()
        {
            if (!IsTrackerOwner()) return;

            playerNames = new string[0];
            isTracking = false;
            RequestSerialization();
        }

        /// <summary>
        /// Adds a single player to the tracked list.
        /// </summary>
        public void AddTrackedPlayer(VRCPlayerApi player)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[1];
            players[0] = player;
            AddTrackedPlayers(players);
        }

        /// <summary>
        /// Removes a single player from the tracked list.
        /// </summary>
        public void RemoveTrackedPlayer(VRCPlayerApi player)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[1];
            players[0] = player;
            RemoveTrackedPlayers(players);
        }

        /// <summary>
        /// Adds multiple players to the tracked list.
        /// </summary>
        public void AddTrackedPlayers(VRCPlayerApi[] players)
        {
            if (!IsTrackerOwner()) return;
            if (players == null || players.Length == 0) return;

            int validNewPlayers = 0;
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]) && !ContainsTrackedPlayer(players[i].displayName))
                {
                    validNewPlayers++;
                }
            }

            if (validNewPlayers == 0) return;

            int currentCount = playerNames.Length;
            string[] newPlayerNames = new string[currentCount + validNewPlayers];

            if (currentCount > 0)
            {
                System.Array.Copy(playerNames, newPlayerNames, currentCount);
            }

            int newIndex = currentCount;
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]) && !ContainsTrackedPlayer(players[i].displayName))
                {
                    newPlayerNames[newIndex] = players[i].displayName;
                    newIndex++;
                }
            }

            playerNames = newPlayerNames;
            RequestSerialization();

            // Broadcast individual player added events
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]))
                {
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastPlayerAdded), players[i].displayName);
                }
            }
        }

        /// <summary>
        /// Removes multiple players from the tracked list.
        /// </summary>
        public void RemoveTrackedPlayers(VRCPlayerApi[] players)
        {
            if (!IsTrackerOwner()) return;
            if (players == null || players.Length == 0 || playerNames.Length == 0) return;

            int[] indicesToRemove = new int[players.Length];
            int removeCount = 0;

            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]))
                {
                    int index = FindTrackedPlayerIndex(players[i].displayName);
                    if (index != -1)
                    {
                        indicesToRemove[removeCount] = index;
                        removeCount++;
                    }
                }
            }

            if (removeCount == 0) return;

            int currentCount = playerNames.Length;
            int newSize = currentCount - removeCount;

            if (newSize == 0)
            {
                playerNames = new string[0];
            }
            else
            {
                // Mark indices to remove
                bool[] toRemove = new bool[currentCount];
                for (int i = 0; i < removeCount; i++)
                {
                    toRemove[indicesToRemove[i]] = true;
                }

                // Build new array excluding marked indices
                string[] newPlayerNames = new string[newSize];
                int newIndex = 0;
                for (int i = 0; i < currentCount; i++)
                {
                    if (!toRemove[i])
                    {
                        newPlayerNames[newIndex] = playerNames[i];
                        newIndex++;
                    }
                }

                playerNames = newPlayerNames;
            }

            RequestSerialization();

            // Broadcast individual player removed events
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]))
                {
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastPlayerRemoved), players[i].displayName);
                }
            }
        }
        #endregion

        #region Protected Methods

        /// <summary>
        /// Gets the array of tracked player names.
        /// </summary>
        protected string[] GetPlayerNames()
        {
            return playerNames;
        }

        /// <summary>
        /// Checks if a player with the given display name is being tracked.
        /// </summary>
        protected bool ContainsTrackedPlayer(string displayName)
        {
            for (int i = 0; i < playerNames.Length; i++)
            {
                if (playerNames[i] == displayName)
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when the tracker's synced data is received via OnDeserialization.
        /// This provides the initial full player list to clients when they join or when the list is updated.
        /// For individual player add/remove events, use OnTrackedPlayerAdded and OnTrackedPlayerRemoved.
        /// </summary>
        protected virtual void OnTrackerSynced(VRCPlayerApi[] players)
        {
            // Override this in child classes to handle initial player list sync
        }

        /// <summary>
        /// Called when a player is added to the tracked list.
        /// </summary>
        protected virtual void OnTrackedPlayerAdded(VRCPlayerApi player)
        {
            // Override this in child classes to handle player additions
        }

        /// <summary>
        /// Called when a player is removed from the tracked list.
        /// </summary>
        protected virtual void OnTrackedPlayerRemoved(VRCPlayerApi player)
        {
            // Override this in child classes to handle player removals
        }

        #endregion

        #region Network Events

        /// <summary>
        /// Network callable event to trigger player added callback on all clients.
        /// </summary>
        [NetworkCallable]
        public void BroadcastPlayerAdded(string playerName)
        {
            VRCPlayerApi player = GetPlayerByName(playerName);
            if (Utilities.IsValid(player))
            {
                OnTrackedPlayerAdded(player);
            }
        }

        /// <summary>
        /// Network callable event to trigger player removed callback on all clients.
        /// </summary>
        [NetworkCallable]
        public void BroadcastPlayerRemoved(string playerName)
        {
            VRCPlayerApi player = GetPlayerByName(playerName);
            if (Utilities.IsValid(player))
            {
                OnTrackedPlayerRemoved(player);
            }
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
        /// Finds the index of a player by display name.
        /// </summary>
        private int FindTrackedPlayerIndex(string displayName)
        {
            for (int i = 0; i < playerNames.Length; i++)
            {
                if (playerNames[i] == displayName)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Gets a VRCPlayerApi by display name.
        /// </summary>
        private VRCPlayerApi GetPlayerByName(string displayName)
        {
            VRCPlayerApi[] allPlayers = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(allPlayers);

            for (int i = 0; i < allPlayers.Length; i++)
            {
                if (Utilities.IsValid(allPlayers[i]) && allPlayers[i].displayName == displayName)
                {
                    return allPlayers[i];
                }
            }
            return null;
        }

        /// <summary>
        /// Gets the VRCPlayerApi objects for all tracked players.
        /// </summary>
        private VRCPlayerApi[] GetPlayerApis()
        {
            int currentCount = playerNames.Length;
            VRCPlayerApi[] players = new VRCPlayerApi[currentCount];
            VRCPlayerApi[] allPlayers = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(allPlayers);

            int foundCount = 0;
            for (int i = 0; i < currentCount; i++)
            {
                string targetName = playerNames[i];

                for (int j = 0; j < allPlayers.Length; j++)
                {
                    if (Utilities.IsValid(allPlayers[j]) && allPlayers[j].displayName == targetName)
                    {
                        players[foundCount++] = allPlayers[j];
                        break;
                    }
                }
            }

            if (foundCount < currentCount)
            {
                VRCPlayerApi[] validPlayers = new VRCPlayerApi[foundCount];
                System.Array.Copy(players, validPlayers, foundCount);
                return validPlayers;
            }

            return players;
        }

        /// <summary>
        /// Triggers the OnPlayersUpdate callback with current tracked players.
        /// </summary>
        private void OnTrackerSynced()
        {
            VRCPlayerApi[] players = GetPlayerApis();
            OnTrackerSynced(players);
        }

        #endregion
    }
}