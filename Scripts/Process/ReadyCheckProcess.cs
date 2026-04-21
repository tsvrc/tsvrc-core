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

        public void _OnTrackingStarted()
        {
            TsEmit(OnReadyCheckStartedEvent);
        }

        public void _OnTrackingStopped()
        {
            TsEmit(OnReadyCheckStoppedEvent);
        }

        public void _OnTrackingCompleted()
        {
            TsEmit(OnReadyCheckCompletedEvent);
        }

        public void _OnTrackingDeserialization() { }

        public void _OnTrackingPlayersAdded() { }

        public void _OnTrackingPlayersRemoved()
        {
            // NotifyTrackedPlayersRemoved fires on all clients. Only the owner has an accurate
            // _readyPlayerIds and should mutate state. Calling directly avoids the N² event
            // storm that would result from every client sending to All.
            if (!IsProcessOwner()) return;

            foreach (string playerId in LastRemovedPlayerIds)
            {
                if (IsPlayerReady(playerId))
                    BroadcastRemoveReadyPlayer(playerId);
            }

            // A non-ready player may have just been removed, making all remaining players ready.
            CheckAllPlayersReady();
        }

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
            if (!IsProcessRunning()) return;

            string playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            if (ready)
            {
                if (IsPlayerReady(playerId)) return;
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastAddReadyPlayer), playerId);
            }
            else
            {
                if (!IsPlayerReady(playerId)) return;
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
