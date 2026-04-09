using Tsvrc.Player;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsAutoPlayerTracker : TsPlayerTracker
    {
        public const string OnAutoTrackingStartedEvent = "OnAutoTrackingStarted";
        public const string OnAutoTrackingStoppedEvent = "OnAutoTrackingStopped";
        public const string OnAutoTrackingCompletedEvent = "OnAutoTrackingCompleted";
        public const string OnAutoTrackingDeserializationEvent = "OnAutoTrackingDeserialization";
        public const string OnAutoTrackingPlayersAddedEvent = "OnAutoTrackingPlayersAdded";
        public const string OnAutoTrackingPlayersRemovedEvent = "OnAutoTrackingPlayersRemoved";

        /// <summary>
        /// Initializes the TsAutoPlayerTracker. Use <see cref="TsvrcBehaviour.TsSubscribe"/> to register
        /// listeners for the events defined as constants on this class.
        /// Read event data from the <c>Last*</c> properties inside your callback methods:
        /// <list type="bullet">
        /// <item><term><see cref="OnAutoTrackingStartedEvent"/></term><description><c>LastPlayerIds</c></description></item>
        /// <item><term><see cref="OnAutoTrackingStoppedEvent"/></term><description><c>LastPlayerIds</c></description></item>
        /// <item><term><see cref="OnAutoTrackingCompletedEvent"/></term><description><c>LastPlayerIds</c></description></item>
        /// <item><term><see cref="OnAutoTrackingDeserializationEvent"/></term><description><c>LastPlayerIds</c></description></item>
        /// <item><term><see cref="OnAutoTrackingPlayersAddedEvent"/></term><description><c>LastAddedPlayerIds</c></description></item>
        /// <item><term><see cref="OnAutoTrackingPlayersRemovedEvent"/></term><description><c>LastRemovedPlayerIds</c></description></item>
        /// </list>
        /// </summary>
        public void TsConstructAutoPlayerTracker()
        {
            TsConstructPlayerTracker();
            TsSubscribe(this, OnTrackingStartedEvent, nameof(_OnTrackingStarted));
            TsSubscribe(this, OnTrackingStoppedEvent, nameof(_OnTrackingStopped));
            TsSubscribe(this, OnTrackingCompletedEvent, nameof(_OnTrackingCompleted));
            TsSubscribe(this, OnTrackingDeserializationEvent, nameof(_OnTrackingDeserialization));
            TsSubscribe(this, OnTrackingPlayersAddedEvent, nameof(_OnTrackingPlayersAdded));
            TsSubscribe(this, OnTrackingPlayersRemovedEvent, nameof(_OnTrackingPlayersRemoved));
        }

        #region TsPlayerTracker Callbacks

        public void _OnTrackingStarted() { TsEmit(OnAutoTrackingStartedEvent); }
        public void _OnTrackingStopped() { TsEmit(OnAutoTrackingStoppedEvent); }
        public void _OnTrackingCompleted() { TsEmit(OnAutoTrackingCompletedEvent); }
        public void _OnTrackingDeserialization() { TsEmit(OnAutoTrackingDeserializationEvent); }
        public void _OnTrackingPlayersAdded() { TsEmit(OnAutoTrackingPlayersAddedEvent); }
        public void _OnTrackingPlayersRemoved() { TsEmit(OnAutoTrackingPlayersRemovedEvent); }

        #endregion

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