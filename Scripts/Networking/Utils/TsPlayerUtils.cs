using VRC.SDKBase;

namespace Tsvrc.TsNetworking.Utils
{
    public class PlayerApiUtility
    {
        /// <summary>
        /// Gets a unique identifier for the player.
        /// This is not the playerId used by VRC, but a custom identifier.
        /// </summary>
        public static string GetPlayerID(VRCPlayerApi player)
        {
            return player.displayName;
        }

        /// <summary>
        /// Finds a player by their unique identifier.
        /// It expects the identifier to be the one returned by GetPlayerID.
        /// </summary>
        public static VRCPlayerApi FindPlayerByID(string playerID)
        {
            VRCPlayerApi[] allPlayers = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(allPlayers);
            foreach (var player in allPlayers)
            {
                if (GetPlayerID(player) == playerID)
                {
                    return player;
                }
            }
            return null;
        }
    }
}