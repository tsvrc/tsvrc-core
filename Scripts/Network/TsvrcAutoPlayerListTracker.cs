using Tsvrc.Player;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.TsNetworking
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcAutoPlayerListTracker : TsvrcPlayerListTracker
    {
        #region VRChat Callbacks

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!IsProcessRunning() || !IsProcessOwner()) return;

            var playerId = TsPlayer.GetPlayerID(player);
            var players = TsPlayer.ToArray(playerId);
            AddTrackedPlayers(players);
        }

        #endregion
    }
}