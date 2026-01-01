using Tsvrc.TsNetworking.Utils;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.TsNetworking
{
    /// <summary>
    /// Automatically tracks all players in the instance.
    /// Only the master manages the list, all clients receive updates via network sync.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcAutoPlayerListTracker : TsvrcPlayerListTracker
    {
        #region VRChat Callbacks
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // Only owner processes player joins
            if (!IsProcessOwner()) return;

            AddTrackedPlayer(TsPlayerUtils.GetPlayerID(player));
        }
        #endregion
    }
}