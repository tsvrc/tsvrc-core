using Tsvrc.Player;
using Tsvrc.Utils;
using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    public class TsReadyCheckProcess : TsPlayerTracker
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

        #region TsvrcProcess Callbacks

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

            string[] trackedPlayerIds = GetTrackedPlayerIds();

            foreach (string playerId in trackedPlayerIds)
            {
                if (!IsPlayerReady(playerId))
                {
                    return; // At least one player is not ready
                }
            }

            CompleteReadyCheck();
        }

        #endregion

        #region TsvrcPlayerListTracker Callbacks

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
            foreach (string playerId in LastRemovedPlayerIds)
            {
                if (IsPlayerReady(playerId))
                {
                    SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastRemoveReadyPlayer), playerId);
                }
            }
        }

        #endregion

        #region Public Methods

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

        #endregion

        #region Protected Methods

        /// <summary>
        /// Checks if a player with the given ID is marked as ready.
        /// </summary>
        protected bool IsPlayerReady(string playerId)
        {
            return TsArray.Contains(_readyPlayerIds, playerId);
        }

        #endregion

        #region Network Events

        /// <summary>
        /// Adds a player to the ready list.
        /// Sent to all players instead of NetworkEventTarget.Owner to avoid a race where
        /// ownership hasn't propagated yet on the sender's side.
        /// </summary>
        [NetworkCallable]
        public void BroadcastAddReadyPlayer(string playerId)
        {
            if (!IsProcessRunning()) return;
            if (!IsProcessOwner()) return;

            string[] playerIds = TsPlayer.ToArray(playerId);
            _readyPlayerIds = TsArray.Add(_readyPlayerIds, playerIds);
            RequestSerialization();
        }

        /// <summary>
        /// Removes a player from the ready list.
        /// See BroadcastAddReadyPlayer for the two-guard reasoning.
        /// </summary>
        [NetworkCallable]
        public void BroadcastRemoveReadyPlayer(string playerId)
        {
            if (!IsProcessRunning()) return;
            if (!IsProcessOwner()) return;

            string[] playerIds = TsPlayer.ToArray(playerId);
            _readyPlayerIds = TsArray.Remove(_readyPlayerIds, playerIds);
            RequestSerialization();
        }

        #endregion
    }
}
