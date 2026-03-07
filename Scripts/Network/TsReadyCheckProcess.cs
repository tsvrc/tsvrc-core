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
        [UdonSynced] private string[] _readyPlayerIds = new string[0];

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

        protected override void OnProcessStartedAsTrackedPlayer(string[] playerIds)
        {
            base.OnProcessStartedAsTrackedPlayer(playerIds);
            OnReadyCheckStartedAsTrackedPlayer(playerIds);
        }

        protected override void OnProcessStoppedAsTrackedPlayer(string[] playerIds)
        {
            base.OnProcessStoppedAsTrackedPlayer(playerIds);
            OnReadyCheckStoppedAsTrackedPlayer(playerIds);
        }

        protected override void OnProcessCompletedAsTrackedPlayer(string[] playerIds)
        {
            base.OnProcessCompletedAsTrackedPlayer(playerIds);
            OnReadyCheckCompletedAsTrackedPlayer(playerIds);
        }

        protected override void OnPlayersRemovedAsTrackedPlayer(string[] playerIds)
        {
            base.OnPlayersRemovedAsTrackedPlayer(playerIds);

            foreach (string playerId in playerIds)
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
            base.StartProcessFromTracker(playerIds, useProcessUpdate: true);
        }

        /// <summary>
        /// Stops the ready check process before completion.
        /// </summary>
        public virtual void StopReadyCheck()
        {
            base.StopProcessFromTracker();
        }

        /// <summary>
        /// Completes the ready check process.
        /// </summary>
        public virtual void CompleteReadyCheck()
        {
            base.CompleteProcessFromTracker();
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

        #region Virtual Methods

        /// <summary>
        /// Called when a ready check is started on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// For owner-only logic, override OnProcessStarted() from the base class.
        /// </summary>
        protected virtual void OnReadyCheckStartedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when a ready check is stopped on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// For owner-only logic, override OnProcessStopped() from the base class.
        /// </summary>
        protected virtual void OnReadyCheckStoppedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when a ready check is completed on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// For owner-only logic, override OnProcessCompleted() from the base class.
        /// </summary>
        protected virtual void OnReadyCheckCompletedAsTrackedPlayer(string[] playerIds) { }

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
