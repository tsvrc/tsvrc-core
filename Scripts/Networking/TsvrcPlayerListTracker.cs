using Tsvrc.Core;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.TsNetworking
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcPlayerListTracker : TsvrcBehaviour
    {
        [UdonSynced] private string[] playerNames = new string[0];

        // Cache to avoid repeated string comparisons
        private int playerCount = 0;

        private void Start()
        {
            // Initialize player count from synced data
            playerCount = playerNames != null ? playerNames.Length : 0;
        }

        public void AddTrackedPlayer(VRCPlayerApi player)
        {
            // Create single-item array and call array method
            VRCPlayerApi[] players = new VRCPlayerApi[1];
            players[0] = player;
            AddTrackedPlayers(players);
        }

        public void RemoveTrackedPlayer(VRCPlayerApi player)
        {
            // Create single-item array and call array method
            VRCPlayerApi[] players = new VRCPlayerApi[1];
            players[0] = player;
            RemoveTrackedPlayers(players);
        }

        public void AddTrackedPlayers(VRCPlayerApi[] players)
        {
            // Only owner can modify the player list
            if (!Networking.IsOwner(gameObject)) return;
            if (players == null || players.Length == 0) return;

            // Count valid new players
            int validNewPlayers = 0;
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]) && !ContainsPlayer(players[i].displayName))
                {
                    validNewPlayers++;
                }
            }

            if (validNewPlayers == 0) return;

            // Resize array
            string[] newPlayerNames = new string[playerCount + validNewPlayers];

            // Copy existing players
            if (playerCount > 0)
            {
                System.Array.Copy(playerNames, newPlayerNames, playerCount);
            }

            // Add new players
            int newIndex = playerCount;
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]) && !ContainsPlayer(players[i].displayName))
                {
                    newPlayerNames[newIndex] = players[i].displayName;
                    newIndex++;
                }
            }

            playerNames = newPlayerNames;
            playerCount += validNewPlayers;

            RequestSerialization();

            // Trigger callbacks for owner
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]))
                {
                    OnPlayerAdded(players[i]);
                }
            }
            OnPlayersUpdate();
        }

        public void RemoveTrackedPlayers(VRCPlayerApi[] players)
        {
            // Only owner can modify the player list
            if (!Networking.IsOwner(gameObject)) return;
            if (players == null || players.Length == 0 || playerCount == 0) return;

            // Find indices to remove
            int[] indicesToRemove = new int[players.Length];
            int removeCount = 0;

            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]))
                {
                    int index = FindPlayerIndex(players[i].displayName);
                    if (index != -1)
                    {
                        indicesToRemove[removeCount] = index;
                        removeCount++;
                    }
                }
            }

            if (removeCount == 0) return;

            // Sort indices in descending order to remove from end first
            System.Array.Sort(indicesToRemove, 0, removeCount);
            System.Array.Reverse(indicesToRemove, 0, removeCount);

            // Calculate new size
            int newSize = playerCount - removeCount;

            if (newSize == 0)
            {
                playerNames = new string[0];
                playerCount = 0;
            }
            else
            {
                // Create new array
                string[] newPlayerNames = new string[newSize];
                int newIndex = 0;

                // Copy players not being removed
                for (int i = 0; i < playerCount; i++)
                {
                    bool shouldRemove = false;
                    for (int j = 0; j < removeCount; j++)
                    {
                        if (indicesToRemove[j] == i)
                        {
                            shouldRemove = true;
                            break;
                        }
                    }

                    if (!shouldRemove)
                    {
                        newPlayerNames[newIndex] = playerNames[i];
                        newIndex++;
                    }
                }

                playerNames = newPlayerNames;
                playerCount = newSize;
            }

            RequestSerialization();

            // Trigger callbacks for owner
            for (int i = 0; i < players.Length; i++)
            {
                if (Utilities.IsValid(players[i]))
                {
                    OnPlayerRemoved(players[i]);
                }
            }
            OnPlayersUpdate();
        }

        // Helper method to check if player exists
        protected bool ContainsPlayer(string displayName)
        {
            for (int i = 0; i < playerCount; i++)
            {
                if (playerNames[i] == displayName)
                {
                    return true;
                }
            }
            return false;
        }

        // Helper method to find player index
        private int FindPlayerIndex(string displayName)
        {
            for (int i = 0; i < playerCount; i++)
            {
                if (playerNames[i] == displayName)
                {
                    return i;
                }
            }
            return -1;
        }

        // Public getter for player count
        public int GetPlayerCount()
        {
            return playerCount;
        }

        /// <summary>
        ///  Gets the list of tracked player names.
        /// </summary>
        /// <returns></returns>
        protected string[] GetPlayerNames()
        {
            return playerNames;
        }

        /// <summary>
        /// Gets the list of tracked player APIs.
        /// </summary>
        /// <returns></returns>
        private VRCPlayerApi[] GetPlayerApis()
        {
            VRCPlayerApi[] players = new VRCPlayerApi[playerCount];
            VRCPlayerApi[] allPlayers = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(allPlayers);

            int foundCount = 0;
            for (int i = 0; i < playerCount; i++)
            {
                string targetName = playerNames[i];

                // Find matching player in current instance
                for (int j = 0; j < allPlayers.Length; j++)
                {
                    if (Utilities.IsValid(allPlayers[j]) && allPlayers[j].displayName == targetName)
                    {
                        players[foundCount++] = allPlayers[j];
                        break;
                    }
                }
            }

            // Return array with only found players (in case some left)
            if (foundCount < playerCount)
            {
                VRCPlayerApi[] validPlayers = new VRCPlayerApi[foundCount];
                System.Array.Copy(players, validPlayers, foundCount);
                return validPlayers;
            }

            return players;
        }

        // Virtual method that can be overridden in child classes
        // Called whenever the player list is updated (add or remove)
        public virtual void OnPlayersUpdate(VRCPlayerApi[] players)
        {
            // Override this in child classes to handle player list updates
        }

        // Virtual method called when a player is added to the list
        public virtual void OnPlayerAdded(VRCPlayerApi player)
        {
            // Override this in child classes to handle player additions
        }

        // Virtual method called when a player is removed from the list
        public virtual void OnPlayerRemoved(VRCPlayerApi player)
        {
            // Override this in child classes to handle player removals
        }

        // Internal method to trigger the callback
        private void OnPlayersUpdate()
        {
            VRCPlayerApi[] players = GetPlayerApis();
            OnPlayersUpdate(players);
        }

#pragma warning disable
        public override void OnPlayerLeft(VRCPlayerApi player)
#pragma warning restore
        {
            // Only owner processes player leaves
            if (!Networking.IsOwner(gameObject)) return;

            RemoveTrackedPlayer(player);
        }

        // Sync callback to update cache when data is received
        public override void OnDeserialization()
        {
            playerCount = playerNames != null ? playerNames.Length : 0;

            // Trigger callback for all clients when they receive updates
            OnPlayersUpdate();
        }
    }
}