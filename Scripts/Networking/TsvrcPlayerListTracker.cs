using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.TsNetworking
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcPlayerListTracker : UdonSharpBehaviour
    {
        [UdonSynced] private string[] playerNames = new string[0];

        // Cache to avoid repeated string comparisons
        private int playerCount = 0;

        private void Start()
        {
            // Initialize player count from synced data
            playerCount = playerNames != null ? playerNames.Length : 0;
        }

        public void AddPlayer(VRCPlayerApi player)
        {
            // Only master can modify the player list
            if (!Networking.IsMaster) return;
            if (!Utilities.IsValid(player)) return;

            string displayName = player.displayName;

            // Quick check using cached count
            if (ContainsPlayer(displayName)) return;

            // Resize array
            string[] newPlayerNames = new string[playerCount + 1];

            // Copy existing players
            if (playerCount > 0)
            {
                System.Array.Copy(playerNames, newPlayerNames, playerCount);
            }

            newPlayerNames[playerCount] = displayName;
            playerNames = newPlayerNames;
            playerCount++;

            RequestSerialization();

            // Trigger callbacks for master
            OnPlayerAdded(player);
            OnPlayersUpdate();
        }

        public void RemovePlayer(VRCPlayerApi player)
        {
            // Only master can modify the player list
            if (!Networking.IsMaster) return;
            if (!Utilities.IsValid(player) || playerCount == 0) return;

            string displayName = player.displayName;
            int indexToRemove = FindPlayerIndex(displayName);

            if (indexToRemove == -1) return;

            // Handle single player case
            if (playerCount == 1)
            {
                playerNames = new string[0];
                playerCount = 0;
            }
            else
            {
                // Create new array
                string[] newPlayerNames = new string[playerCount - 1];

                // Copy elements before removed index
                if (indexToRemove > 0)
                {
                    System.Array.Copy(playerNames, 0, newPlayerNames, 0, indexToRemove);
                }

                // Copy elements after removed index
                if (indexToRemove < playerCount - 1)
                {
                    System.Array.Copy(playerNames, indexToRemove + 1, newPlayerNames, indexToRemove, playerCount - indexToRemove - 1);
                }

                playerNames = newPlayerNames;
                playerCount--;
            }

            RequestSerialization();

            // Trigger callbacks for master
            OnPlayerRemoved(player);
            OnPlayersUpdate();
        }

        // Helper method to check if player exists
        private bool ContainsPlayer(string displayName)
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

        // Get VRCPlayerApi array from stored player names
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
            // Only master processes player leaves
            if (!Networking.IsMaster) return;

            RemovePlayer(player);
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