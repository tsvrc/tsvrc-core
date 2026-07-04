using Tsvrc.Player;
using Tsvrc.Utils;
using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Tracking
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

        // Tracks whether a ready check is active on this client. Used in SetReady() instead of
        // IsProcessRunning() to avoid a race where the synced _isRunning has not arrived yet when
        // SetReady() is called from inside a network event handler. VRChat delivers synced
        // variables and network events through separate subsystems with no ordering guarantee.
        // Updated by Notify* events (which are ordered relative to each other from the same
        // sender) and corrected by OnTrackingDeserialization for late joiners or missed events.
        private bool _readyCheckActive = false;

        protected override void OnProcessStarted()
        {
            // Clear _readyPlayerIds before calling base. PlayerTracker.OnProcessStarted fires
            // NotifyTrackedPlayersProcessStarted inline, which calls OnTrackingStarted and then
            // OnReadyCheckStarted before returning. If a restart is triggered from an inline
            // OnReadyCheckCompleted callback, the old _readyPlayerIds would still be visible
            // during OnReadyCheckStarted, and any SetReady() call there would silently do
            // nothing because IsPlayerReady would return true for players from the old run.
            _readyPlayerIds = new string[0];
            base.OnProcessStarted();
            // No explicit RequestSerialization here. PlayerTracker.OnProcessStarted already
            // calls it after assigning _trackedPlayerIds, and since _readyPlayerIds was cleared
            // above before that call, the single packet captures both arrays. VRChat coalesces
            // multiple RequestSerialization calls in the same frame into one outbound packet.
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            // If a new process was started from an inline callback, _readyPlayerIds is already
            // empty (cleared in OnProcessStarted) and _readyCheckActive is already true (set
            // by the inline NotifyTrackedPlayersProcessStarted which calls OnTrackingStarted).
            // Clearing _readyCheckActive here would silently block all SetReady() calls on
            // every client for the new process, so we skip cleanup when a process is running.
            if (IsProcessRunning()) return;

            _readyPlayerIds = new string[0];
            // No explicit RequestSerialization here. TsvrcProcess.InternalCleanup always calls
            // it after OnProcessCleanup returns, so _readyPlayerIds=[] is captured by that packet.

            // Reset the flag so SetReady() does nothing until the next StartReadyCheck.
            // OnTrackingStopped and OnTrackingCompleted cover the normal stop and complete paths.
            // This also handles InternalCleanup calls that bypass the normal stop/complete flow.
            _readyCheckActive = false;
        }

        protected override void OnTrackingDeserialization()
        {
            // Late joiners never receive NotifyTrackedPlayersProcessStarted because network
            // events are not replayed, so _readyCheckActive would stay false and block SetReady().
            // Deriving it from the synced _isRunning fixes this. Also corrects missed stop/complete
            // events. OnDeserialization always fires after all synced variables are written, so
            // _isRunning is already up to date when we read it here.
            _readyCheckActive = IsProcessRunning();
        }

        protected override void OnProcessUpdate()
        {
            base.OnProcessUpdate();
            CheckAllPlayersReady();
        }

        // Completes the ready check if every tracked player is ready.
        // Called immediately after each player marks ready and also on every 0.5s poll tick.
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
            // NotifyTrackedPlayersRemoved fires on all clients, but only the owner holds an
            // accurate _readyPlayerIds and should mutate it. Having every client send to the
            // owner would cause N² network calls with N players, so we guard here.
            if (!IsProcessOwner()) return;

            // Collect all ready players to remove in one pass, then call TsArray.Remove once.
            // Doing one removal per player would allocate a new array each time and iterate
            // _readyPlayerIds on every call. Batching reduces this to two passes and two
            // allocations regardless of how many players are removed. This matters because
            // OnOwnerAbandonedProcess can remove up to 79 players at once.
            string[] readyToRemove = new string[removedPlayerIds.Length];
            int removeCount = 0;
            foreach (string playerId in removedPlayerIds)
            {
                if (IsPlayerReady(playerId))
                    readyToRemove[removeCount++] = playerId;
            }
            if (removeCount > 0)
            {
                if (removeCount < readyToRemove.Length)
                {
                    string[] trimmed = new string[removeCount];
                    System.Array.Copy(readyToRemove, trimmed, removeCount);
                    readyToRemove = trimmed;
                }
                // RemoveReadyPlayerInternal cannot be used here because VRChat keeps CallingPlayer
                // active for the entire call chain until the outermost network call returns. If we
                // arrive here from a network event, CallingPlayer belongs to the original sender
                // and the spoofing guard in BroadcastRemoveReadyPlayer would reject the call. We
                // mutate directly since we already confirmed each ID is valid via IsPlayerReady.
                _readyPlayerIds = TsArray.Remove(_readyPlayerIds, readyToRemove);
                RequestSerialization();
            }

            // Removing a non-ready player may make all remaining tracked players ready.
            CheckAllPlayersReady();
        }

        /// <summary>Called on all clients when the ready check starts. Override to react locally.</summary>
        protected virtual void OnReadyCheckStarted() { }
        /// <summary>Called on all clients when the ready check is stopped before completion. Override to react locally.</summary>
        protected virtual void OnReadyCheckStopped() { }
        /// <summary>Called on all clients when all tracked players are ready. Override to react locally.</summary>
        protected virtual void OnReadyCheckCompleted() { }

        /// <summary>
        /// Starts the ready check for the given set of players.
        /// </summary>
        /// <param name="playerIds">The player IDs to include in the ready check.</param>
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
        /// <param name="ready">Pass false to mark the local player as not ready.</param>
        public void SetReady(bool ready = true)
        {
            // Use _readyCheckActive instead of IsProcessRunning(). IsProcessRunning reads the
            // synced _isRunning which may not have arrived yet when called from inside a network
            // event handler. _readyCheckActive is driven by ordered events and is reliable here.
            if (!_readyCheckActive) return;

            string playerId = _localPlayerId;

            // On the owner, routing through SendCustomNetworkEvent would be rejected by the
            // spoofing guard in BroadcastAddReadyPlayer. VRChat keeps CallingPlayer active for
            // the entire call chain until the outermost network call returns, so if SetReady is
            // called from inside a network event, CallingPlayer belongs to the original sender
            // and the guard would reject the call. We mutate directly instead.
            // IsProcessRunning is checked explicitly because _readyCheckActive can lag behind
            // the true process state on the owner.
            if (IsProcessOwner())
            {
                if (!IsProcessRunning()) return;
                if (ready)
                {
                    if (!IsTrackedPlayer(playerId) || IsPlayerReady(playerId)) return;
                    _readyPlayerIds = TsArray.Add(_readyPlayerIds, TsPlayer.ToArray(playerId));
                    RequestSerialization();
                    CheckAllPlayersReady();
                }
                else
                {
                    RemoveReadyPlayerInternal(playerId);
                }
                return;
            }

            // Route to the owner since only the owner mutates _readyPlayerIds. Sending to All
            // would cause N² deliveries with N players. We skip a local duplicate check since
            // _readyPlayerIds can be stale on non-owners and the owner deduplicates on arrival.
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
        /// Returns true if the player with the given ID is currently marked as ready.
        /// </summary>
        protected bool IsPlayerReady(string playerId)
        {
            return TsArray.Contains(_readyPlayerIds, playerId);
        }

        // Removes a player without going through the network event path. We cannot call
        // BroadcastRemoveReadyPlayer here because VRChat keeps CallingPlayer active for the
        // entire chain until the outermost network call returns. If this runs inside a network
        // event, CallingPlayer would not match the player being removed and the spoofing guard
        // in BroadcastRemoveReadyPlayer would reject the call.
        private void RemoveReadyPlayerInternal(string playerId)
        {
            if (!IsPlayerReady(playerId)) return;
            _readyPlayerIds = TsArray.Remove(_readyPlayerIds, TsPlayer.ToArray(playerId));
            RequestSerialization();
        }

        /// <summary>
        /// Adds a player to the ready list. Only the owner processes the change.
        /// </summary>
        // Rate limited to 2 per second. VRChat recommends keeping this as low as possible
        // to prevent abuse. A player only needs to toggle ready a couple of times per check.
        [NetworkCallable(maxEventsPerSecond: 2)]
        public void BroadcastAddReadyPlayer(string playerId)
        {
            if (!IsProcessRunning() || !IsProcessOwner()) return;
            if (string.IsNullOrEmpty(playerId)) return;
            // Only allow a player to mark themselves as ready. CallingPlayer is null for
            // direct local calls so the check is skipped in that case.
            var caller = NetworkCalling.CallingPlayer;
            if (caller != null && TsPlayer.GetPlayerID(caller) != playerId) return;
            if (!IsTrackedPlayer(playerId) || IsPlayerReady(playerId)) return;

            _readyPlayerIds = TsArray.Add(_readyPlayerIds, TsPlayer.ToArray(playerId));
            RequestSerialization();
            // Check immediately instead of waiting for the next poll tick.
            CheckAllPlayersReady();
        }

        /// <summary>
        /// Removes a player from the ready list. Only the owner processes the change.
        /// </summary>
        // Rate limited to 2 per second, same reason as BroadcastAddReadyPlayer.
        [NetworkCallable(maxEventsPerSecond: 2)]
        public void BroadcastRemoveReadyPlayer(string playerId)
        {
            if (!IsProcessRunning() || !IsProcessOwner()) return;
            if (string.IsNullOrEmpty(playerId)) return;
            // Only allow a player to unready themselves. CallingPlayer is null for direct
            // local calls so the check is skipped in that case.
            var caller = NetworkCalling.CallingPlayer;
            if (caller != null && TsPlayer.GetPlayerID(caller) != playerId) return;
            RemoveReadyPlayerInternal(playerId);
        }
    }
}
