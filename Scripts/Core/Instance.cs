using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.Core
{
    public class Instance : UdonSharpBehaviour
    {
        public VRCPlayerApi[] getPlayers()
        {
            int playersCount = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi[] players = new VRCPlayerApi[playersCount];
            VRCPlayerApi.GetPlayers(players);

            return players;
        }
    }
}