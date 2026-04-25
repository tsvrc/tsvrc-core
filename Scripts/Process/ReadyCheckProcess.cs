using Tsvrc.Network;
using Tsvrc.Player;
using Tsvrc.Utils;
using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Process
{
    /// <summary>
    /// A <see cref="PlayerTracker"/> that runs a ready check over a set of tracked players.
    /// The owner polls readiness every 0.5 s and completes immediately when all tracked players
    /// are ready. Any player can call <see cref="SetReady"/> to mark themselves ready or unready.
    /// Subscribe via the <c>OnReadyCheck*Event</c> string constants and read
    /// <see cref="PlayerTracker.LastPlayerIds"/> in your callback.
    /// </summary>
    public class ReadyCheckProcess : PlayerTracker
    {
        /// <summary>
        /// Emitted when the ready check starts.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnReadyCheckStartedEvent = "OnReadyCheckStarted";
        /// <summary>
        /// Emitted when the ready check is stopped before completion.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnReadyCheckStoppedEvent = "OnReadyCheckStopped";
        /// <summary>
        /// Emitted when all tracked players are ready.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnReadyCheckCompletedEvent = "OnReadyCheckCompleted";

        [UdonSynced] private string[] _readyPlayerIds = new string[0];

        // Network-event-driven flag: true while a ready check is in progress on this client.
        // Used in SetReady() instead of IsProcessRunning() ([UdonSynced] _isRunning) to avoid a
        // serialization-vs-event race: manual-sync packets and network events travel through
        // separate VRChat subsystems with no ordering guarantee between them, so _isRunning=true
        // may not yet have arrived when SetReady() is called from inside a network event handler.
        // Set/cleared by Notify* network events (ordered with all other events from the same
        // sender); corrected by OnTrackingDeserialization for late joiners and missed events.
        private bool _readyCheckActive = false;

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            _readyPlayerIds = new string[0];
            RequestSerialization();
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            _readyPlayerIds = new string[0];
            RequestSerialization();

            // Reset local flag so SetReady() is inert until the next StartReadyCheck.
            // OnTrackingStopped/OnTrackingCompleted handle the normal stop/complete paths.
            // Resetting here also covers InternalCleanup calls that bypass ExecuteStop().
            _readyCheckActive = false;
        }

        protected override void OnTrackingDeserialization()
        {
            // Late-joiner fix: network events are not repeated for late joiners, so a player
            // who joins mid-check never receives NotifyTrackedPlayersProcessStarted and
            // _readyCheckActive stays false, silently blocking their SetReady() calls.
            // Also corrects missed stop/complete events. _isRunning is already up-to-date
            // here because OnDeserialization fires after all synced variables are written.
            _readyCheckActive = IsProcessRunning();
        }

        protected override void OnProcessUpdate()
        {
            base.OnProcessUpdate();
            CheckAllPlayersReady();
        }

        // Completes the ready check if all tracked players are ready.
        // Called from both BroadcastAddReadyPlayer (event-driven) and OnProcessUpdate (safety-net poll).
        private void CheckAllPlayersReady()
        {
            string[] trackedPlayerIds = GetTrackedPlayerIds();
            if (trackedPlayerIds.Length == 0) return;

            foreach (string playerId in trackedPlayerIds)
            {
                if (!IsPlayerReady(playerId)) return;
            }

            CompleteReadyCheck();
        }

        protected override void OnTrackingStarted(string[] playerIds)
        {
            _readyCheckActive = true;
            OnReadyCheckStarted();
            TsEmit(OnReadyCheckStartedEvent);
        }

        protected override void OnTrackingStopped(string[] playerIds)
        {
            _readyCheckActive = false;
            OnReadyCheckStopped();
            TsEmit(OnReadyCheckStoppedEvent);
        }

        protected override void OnTrackingCompleted(string[] playerIds)
        {
            _readyCheckActive = false;
            OnReadyCheckCompleted();
            TsEmit(OnReadyCheckCompletedEvent);
        }

        protected override void OnTrackingPlayersRemoved(string[] removedPlayerIds)
        {
            // NotifyTrackedPlayersRemoved fires on all clients. Only the owner has an accurate
            // _readyPlayerIds and should mutate state. Calling directly avoids the N² event
            // storm that would result from every client sending to All.
            if (!IsProcessOwner()) return;

            foreach (string playerId in removedPlayerIds)
            {
                if (IsPlayerReady(playerId))
                    RemoveReadyPlayerInternal(playerId);
            }

            // A non-ready player may have just been removed, making all remaining players ready.
            CheckAllPlayersReady();
        }

        /// <summary>Called on all clients when the ready check starts. Override to react locally.</summary>
        protected virtual void OnReadyCheckStarted() { }
        /// <summary>Called on all clients when the ready check is stopped before completion. Override to react locally.</summary>
        protected virtual void OnReadyCheckStopped() { }
        /// <summary>Called on all clients when all tracked players are ready. Override to react locally.</summary>
        protected virtual void OnReadyCheckCompleted() { }

        /// <summary>
        /// Starts the ready check process.
        /// </summary>
        public virtual void StartReadyCheck(string[] playerIds)
        {
            base.StartPlayerTracking(playerIds, useProcessUpdate: true);
        }

        /// <summary>
        /// Stops the ready check process before completion.
        /// </summary>
        public virtual void StopReadyCheck()
        {
            base.StopPlayerTracking();
        }

        /// <summary>
        /// Completes the ready check process.
        /// </summary>
        public virtual void CompleteReadyCheck()
        {
            base.CompletePlayerTracking();
        }

        /// <summary>
        /// Sets the local player's ready status.
        /// </summary>
        public void SetReady(bool ready = true)
        {
            // _readyCheckActive is a local event-driven flag, safe to read from inside network
            // event handlers. IsProcessRunning() reads [UdonSynced] _isRunning which may not have
            // arrived yet when this is called from a network event handler in the same frame.
            if (!_readyCheckActive) return;

            string playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            // No local dedup check: _readyPlayerIds can be stale on non-owners; the owner's
            // Broadcast* methods deduplicate before mutating, so redundant sends are harmless.
            //
            // NetworkEventTarget.Owner: only the owner processes these calls. Routing to All
            // with N players calling SetReady would cause N² deliveries; Owner reduces this to N.
            if (ready)
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(BroadcastAddReadyPlayer), playerId);
            }
            else
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(BroadcastRemoveReadyPlayer), playerId);
            }
        }

        /// <summary>
        /// Checks if a player with the given ID is marked as ready.
        /// </summary>
        protected bool IsPlayerReady(string playerId)
        {
            return TsArray.Contains(_readyPlayerIds, playerId);
        }

        // Internal mutation helper: removes a player without network/caller validation.
        // BroadcastRemoveReadyPlayer cannot be used here because CallingPlayer propagates
        // through the entire call chain (VRChat docs: "InNetworkCall is only reset once the
        // entry function terminates"). If this path is reached from within a [NetworkCallable]
        // context, CallingPlayer ≠ removedPlayerId and the spoofing guard would reject the call.
        private void RemoveReadyPlayerInternal(string playerId)
        {
            if (!IsPlayerReady(playerId)) return;
            _readyPlayerIds = TsArray.Remove(_readyPlayerIds, TsPlayer.ToArray(playerId));
            RequestSerialization();
        }

        /// <summary>
        /// Adds a player to the ready list. Only the owner processes the change.
        /// </summary>
        // maxEventsPerSecond: 2. Per VRChat docs: "It is strongly recommended to set this
        // value as low as you can to mitigate malicious actors abusing your events." A player
        // only needs to toggle ready once or twice per ready check; 2/s is more than enough.
        [NetworkCallable(maxEventsPerSecond: 2)]
        public void BroadcastAddReadyPlayer(string playerId)
        {
            if (!IsProcessRunning() || !IsProcessOwner()) return;
            if (string.IsNullOrEmpty(playerId)) return;
            // Prevent a player from marking someone else as ready: when this arrives over the
            // network the calling player must be the one they claim to be. CallingPlayer is null
            // for direct (non-network) calls, in which case the identity check is skipped.
            var caller = NetworkCalling.CallingPlayer;
            if (caller != null && TsPlayer.GetPlayerID(caller) != playerId) return;
            if (!IsTrackedPlayer(playerId) || IsPlayerReady(playerId)) return;

            _readyPlayerIds = TsArray.Add(_readyPlayerIds, TsPlayer.ToArray(playerId));
            RequestSerialization();
            // Event-driven completion: check immediately rather than waiting for the next poll.
            CheckAllPlayersReady();
        }

        /// <summary>
        /// Removes a player from the ready list. Only the owner processes the change.
        /// </summary>
        // maxEventsPerSecond: 2. Same rationale as BroadcastAddReadyPlayer.
        [NetworkCallable(maxEventsPerSecond: 2)]
        public void BroadcastRemoveReadyPlayer(string playerId)
        {
            if (!IsProcessRunning() || !IsProcessOwner()) return;
            if (string.IsNullOrEmpty(playerId)) return;
            // Spoofing guard: the calling player must be the one they claim to be.
            // CallingPlayer is null outside a network call, in which case the check is skipped.
            var caller = NetworkCalling.CallingPlayer;
            if (caller != null && TsPlayer.GetPlayerID(caller) != playerId) return;
            RemoveReadyPlayerInternal(playerId);
        }
    }
}
