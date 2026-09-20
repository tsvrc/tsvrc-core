using Tsvrc.Core;
using Tsvrc.Player;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.Tracking
{
    /// <summary>
    /// A <see cref="PlayerTracker"/> that automatically tracks every player in the instance.
    /// Players are added when they join and removed when they leave.
    /// Subscribe via the <c>OnAutoTracking*Event</c> string constants and read the <c>Last*</c>
    /// properties in your callback. Call <see cref="StartAutoTracking"/> to begin.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [TsWorldExtensionPoint("TsAutoPlayerTracker")]
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

        /// <summary>
        /// Starts tracking all players currently in the instance.
        /// Any player who joins after this call is automatically added; any who leave are removed.
        /// </summary>
        /// <remarks>
        /// The snapshot of current players is taken at the moment this method is called, not
        /// inside <c>OnPlayerJoined</c>. VRChat docs (event-nodes#onplayerjoined): "When you
        /// join an instance, you execute OnPlayerJoined for every player in the instance,
        /// including yourself." That initial wave fires before any user code runs, so
        /// <c>IsProcessRunning()</c> is still false and all those events are dropped. By the
        /// time this method is called the wave is already gone; <c>GetAllPlayerIDs()</c> here
        /// captures every fully-joined player at exactly the right moment.
        ///
        /// Any player still mid-joining at this instant has not yet fired their
        /// <c>OnPlayerJoined</c>; it will fire after the process starts and be handled there.
        ///
        /// UdonSharp runs on Unity's single main thread, so no <c>OnPlayerJoined</c> event
        /// can interleave between <c>GetAllPlayerIDs()</c> and the process going live.
        /// </remarks>
        public void StartAutoTracking()
        {
            StartAutoTrackingSnapshot();
        }

        /// <summary>
        /// Stops auto-tracking before completion. All clients are notified and the tracked player list is cleared.
        /// </summary>
        public void StopAutoTracking()
        {
            base.StopPlayerTracking();
        }

        /// <summary>
        /// Marks auto-tracking as successfully completed. All clients are notified and the tracked player list is cleared.
        /// </summary>
        public void CompleteAutoTracking()
        {
            base.CompletePlayerTracking();
        }

        /// <summary>
        /// Redirects to the auto-snapshot; both parameters are ignored.
        /// <see cref="AutoPlayerTracker"/> always tracks all players in the instance and never
        /// uses the process-update loop. Use <see cref="StartAutoTracking"/> for the intention-clear entry point.
        /// </summary>
        public override void StartPlayerTracking(string[] playerIds, bool useProcessUpdate = false)
        {
            StartAutoTrackingSnapshot();
        }

        // PlayerTracker.StartPlayerTracking discards its playerIds argument unread
        // whenever the process is already running, so the real player-list snapshot is
        // only computed when a fresh start is actually possible.
        private void StartAutoTrackingSnapshot()
        {
            if (IsProcessRunning())
            {
                base.StartPlayerTracking(null, false);
                return;
            }

            base.StartPlayerTracking(GetActiveNonSuspendedPlayerIDs(), false);
        }

        // PlayerTracker.OnPlayerSuspendChanged only reacts to a *transition* to suspended, and
        // treats waking up as a no-op on the assumption a player was already removed on the way
        // in. A player already suspended before this snapshot runs has no transition left to
        // produce, so it must never enter _trackedPlayerIds here - the same invariant
        // PlayerTracker.OnBecameProcessOwner and ChunkedTransferSession._StartNextReadyCheck
        // already enforce for their own tracked-player scans.
        private static string[] GetActiveNonSuspendedPlayerIDs()
        {
            VRCPlayerApi[] allPlayers = TsPlayer.GetAllPlayers();
            string[] activeIds = new string[allPlayers.Length];
            int activeCount = 0;
            for (int i = 0; i < allPlayers.Length; i++)
            {
                if (!allPlayers[i].isSuspended)
                    activeIds[activeCount++] = TsPlayer.GetPlayerID(allPlayers[i]);
            }
            if (activeCount < allPlayers.Length)
            {
                string[] trimmed = new string[activeCount];
                System.Array.Copy(activeIds, trimmed, activeCount);
                activeIds = trimmed;
            }
            return activeIds;
        }

        protected override void OnTrackingStarted(string[] playerIds) => TsEmit(OnAutoTrackingStartedEvent);
        protected override void OnTrackingStopped(string[] playerIds) => TsEmit(OnAutoTrackingStoppedEvent);
        protected override void OnTrackingCompleted(string[] playerIds) => TsEmit(OnAutoTrackingCompletedEvent);
        protected override void OnTrackingDeserialization() => TsEmit(OnAutoTrackingDeserializationEvent);
        protected override void OnTrackingPlayersAdded(string[] added) => TsEmit(OnAutoTrackingPlayersAddedEvent);
        protected override void OnTrackingPlayersRemoved(string[] removed) => TsEmit(OnAutoTrackingPlayersRemovedEvent);

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            // VRChat: player refs can be invalid; accessing them silently crashes the UdonBehaviour.
            if (!player.IsValid()) return;
            if (!IsProcessRunning() || !IsProcessOwner()) return;

            var playerId = TsPlayer.GetPlayerID(player);
            var players = TsPlayer.ToArray(playerId);
            AddTrackedPlayers(players);
        }
    }
}