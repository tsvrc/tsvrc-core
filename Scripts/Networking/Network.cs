using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.TsNetworking
{
    public class Network : UdonSharpBehaviour
    {
        public VRCPlayerApi[] getInstancePlayers()
        {
            int playersCount = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi[] players = new VRCPlayerApi[playersCount];
            VRCPlayerApi.GetPlayers(players);

            return players;
        }
    }
}