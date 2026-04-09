using Tsvrc.Player;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsAutoPlayerTracker : TsPlayerTracker
    {
        /// <summary>
        /// Emitted when auto-tracking starts.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnAutoTrackingStartedEvent = "OnAutoTrackingStarted";
        /// <summary>
        /// Emitted when auto-tracking stops.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnAutoTrackingStoppedEvent = "OnAutoTrackingStopped";
        /// <summary>
        /// Emitted when auto-tracking completes.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnAutoTrackingCompletedEvent = "OnAutoTrackingCompleted";
        /// <summary>
        /// Emitted on deserialization.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnAutoTrackingDeserializationEvent = "OnAutoTrackingDeserialization";
        /// <summary>
        /// Emitted when players are added.
        /// Read <c>LastAddedPlayerIds</c> in your callback.
        /// </summary>
        public const string OnAutoTrackingPlayersAddedEvent = "OnAutoTrackingPlayersAdded";
        /// <summary>
        /// Emitted when players are removed.
        /// Read <c>LastRemovedPlayerIds</c> in your callback.
        /// </summary>
        public const string OnAutoTrackingPlayersRemovedEvent = "OnAutoTrackingPlayersRemoved";

        protected override void TsStart()
        {
            base.TsStart();
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