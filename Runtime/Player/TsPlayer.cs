using VRC.SDKBase;

namespace Tsvrc.Player
{
    public static class TsPlayer
    {
        /// <summary>
        /// Gets a unique identifier for the player.
        /// This is not the playerId used by VRC, but a custom identifier.
        /// Format: displayName#playerId
        /// </summary>
        public static string GetPlayerID(VRCPlayerApi player)
        {
            return player.displayName + "#" + player.playerId;
        }

        /// <summary>
        /// Extracts the numeric id after the last '#' in a <see cref="GetPlayerID"/> string.
        /// Parsed manually, not with <c>int.Parse</c>, for UdonSharp compatibility. Returns 0
        /// instead of throwing if there's no '#' or no digits after it.
        /// </summary>
        public static int GetNumericPlayerId(string playerId)
        {
            int result = 0;
            int multiplier = 1;
            for (int i = playerId.Length - 1; i >= 0; i--)
            {
                char c = playerId[i];
                if (c == '#') return result;
                if (c < '0' || c > '9') return 0;
                result += (c - '0') * multiplier;
                multiplier *= 10;
            }
            return 0;
        }

        /// <summary>
        /// Finds a player by their unique identifier.
        /// It expects the identifier to be the one returned by GetPlayerID.
        /// </summary>
        public static VRCPlayerApi FindPlayerByID(string playerID)
        {
            return FindPlayerByIDIn(GetAllPlayers(), playerID);
        }

        private static VRCPlayerApi FindPlayerByIDIn(VRCPlayerApi[] players, string playerID)
        {
            foreach (var player in players)
            {
                if (GetPlayerID(player) == playerID)
                {
                    return player;
                }
            }
            return null;
        }

        /// <summary>
        /// Converts an array of VRCPlayerApi to an array of player IDs.
        /// </summary>
        public static string[] ToPlayerIDs(VRCPlayerApi[] players)
        {
            string[] playerIds = new string[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                playerIds[i] = GetPlayerID(players[i]);
            }
            return playerIds;
        }

        /// <summary>
        /// Converts an array of player IDs to an array of VRCPlayerApi.
        /// Only returns valid players that are currently in the instance.
        /// </summary>
        public static VRCPlayerApi[] ToPlayerApis(string[] playerIds)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[playerIds.Length];
            if (playerIds.Length == 0)
            {
                return players;
            }

            // Fetched once and reused below so an N-id lookup costs one player-list
            // fetch instead of N.
            VRCPlayerApi[] allPlayers = GetAllPlayers();
            int foundCount = 0;

            for (int i = 0; i < playerIds.Length; i++)
            {
                VRCPlayerApi player = FindPlayerByIDIn(allPlayers, playerIds[i]);
                if (player != null)
                {
                    players[foundCount++] = player;
                }
            }

            if (foundCount < playerIds.Length)
            {
                VRCPlayerApi[] trimmed = new VRCPlayerApi[foundCount];
                System.Array.Copy(players, trimmed, foundCount);
                return trimmed;
            }

            return players;
        }

        /// <summary>
        /// Gets all players currently in the instance.
        /// </summary>
        public static VRCPlayerApi[] GetAllPlayers()
        {
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            return players;
        }

        /// <summary>
        /// Gets the unique IDs of all players currently in the instance.
        /// </summary>
        public static string[] GetAllPlayerIDs()
        {
            VRCPlayerApi[] players = GetAllPlayers();
            return ToPlayerIDs(players);
        }

        /// <summary>
        /// Creates an array containing a single player ID.
        /// </summary>
        public static string[] ToArray(string playerId)
        {
            var arr = new string[1];
            arr[0] = playerId;
            return arr;
        }
    }
}