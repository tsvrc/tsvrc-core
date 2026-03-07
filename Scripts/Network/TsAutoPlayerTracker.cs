using Tsvrc.Player;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsAutoPlayerTracker : TsPlayerTracker
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