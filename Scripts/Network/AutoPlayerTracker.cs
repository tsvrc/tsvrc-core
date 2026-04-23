using Tsvrc.Player;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AutoPlayerTracker : PlayerTracker
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

        #region PlayerTracker Overrides

        protected override void OnTrackingStarted(string[] playerIds) => TsEmit(OnAutoTrackingStartedEvent);
        protected override void OnTrackingStopped(string[] playerIds) => TsEmit(OnAutoTrackingStoppedEvent);
        protected override void OnTrackingCompleted(string[] playerIds) => TsEmit(OnAutoTrackingCompletedEvent);
        protected override void OnTrackingDeserialization() => TsEmit(OnAutoTrackingDeserializationEvent);
        protected override void OnTrackingPlayersAdded(string[] added) => TsEmit(OnAutoTrackingPlayersAddedEvent);
        protected override void OnTrackingPlayersRemoved(string[] removed) => TsEmit(OnAutoTrackingPlayersRemovedEvent);

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