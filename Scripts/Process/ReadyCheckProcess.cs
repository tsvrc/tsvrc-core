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
        // _readyCheckActive is set/cleared purely by the Notify* network events that PlayerTracker
        // already sends, which ARE ordered with all other events from the same sender.
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
                    BroadcastRemoveReadyPlayer(playerId);
            }

            // A non-ready player may have just been removed, making all remaining players ready.
            CheckAllPlayersReady();
        }

        protected virtual void OnReadyCheckStarted() { }
        protected virtual void OnReadyCheckStopped() { }
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
            // _readyCheckActive is a local event-driven flag — safe to read from inside network
            // event handlers. IsProcessRunning() reads [UdonSynced] _isRunning which may not have
            // arrived yet when this is called from a network event handler in the same frame.
            if (!_readyCheckActive) return;

            string playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            // IsPlayerReady() reads [UdonSynced] _readyPlayerIds which can be stale (e.g. between
            // chunks the owner resets _readyPlayerIds and serializes, but that packet may not have
            // arrived before this event fires). The owner's BroadcastAddReadyPlayer /
            // BroadcastRemoveReadyPlayer both deduplicate before mutating, so extra sends are
            // harmless and the deduplication guards here are not needed.
            if (ready)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastAddReadyPlayer), playerId);
            }
            else
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastRemoveReadyPlayer), playerId);
            }
        }

        /// <summary>
        /// Checks if a player with the given ID is marked as ready.
        /// </summary>
        protected bool IsPlayerReady(string playerId)
        {
            return TsArray.Contains(_readyPlayerIds, playerId);
        }

        /// <summary>
        /// Adds a player to the ready list. Only the owner processes the change.
        /// </summary>
        [NetworkCallable]
        public void BroadcastAddReadyPlayer(string playerId)
        {
            if (!IsProcessRunning() || !IsProcessOwner()) return;
            if (string.IsNullOrEmpty(playerId)) return;
            if (!IsTrackedPlayer(playerId) || IsPlayerReady(playerId)) return;

            _readyPlayerIds = TsArray.Add(_readyPlayerIds, TsPlayer.ToArray(playerId));
            RequestSerialization();
            // Event-driven completion: check immediately rather than waiting for the next poll.
            CheckAllPlayersReady();
        }

        /// <summary>
        /// Removes a player from the ready list. Only the owner processes the change.
        /// </summary>
        [NetworkCallable]
        public void BroadcastRemoveReadyPlayer(string playerId)
        {
            if (!IsProcessRunning() || !IsProcessOwner()) return;
            if (string.IsNullOrEmpty(playerId) || !IsPlayerReady(playerId)) return;

            _readyPlayerIds = TsArray.Remove(_readyPlayerIds, TsPlayer.ToArray(playerId));
            RequestSerialization();
        }
    }
}
