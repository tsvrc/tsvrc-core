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
        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // Only master processes player joins
            if (!Networking.IsMaster) return;

            AddPlayer(player);
        }
    }
}