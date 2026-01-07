using Tsvrc.TsNetworking.Utils;
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

            var playerId = TsPlayerUtils.GetPlayerID(player);
            var players = TsPlayerUtils.ToArray(playerId);
            AddTrackedPlayers(players);
        }

        #endregion
    }
}