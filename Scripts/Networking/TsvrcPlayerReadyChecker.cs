using Tsvrc.List.Utils;
using Tsvrc.TsNetworking.Utils;
using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.TsNetworking
{
    public class TsvrcPlayerReadyChecker : TsvrcPlayerListTracker
    {
        [UdonSynced] private string[] _readyPlayerIds = new string[0];

        // Temporary storage for expected player IDs during process start and completion.
        private string[] _tempPlayerIds = new string[0];

        #region Unity Lifecycle

        private void Update()
        {
            if (!IsRunning() || !IsOwner()) return;

            string[] trackedPlayerIds = GetTrackedPlayerIds();

            foreach (string playerId in trackedPlayerIds)
            {
                if (!IsPlayerReady(playerId))
                {
                    return; // At least one player is not ready
                }
            }

            _tempPlayerIds = trackedPlayerIds;
            CompleteProcess();
        }

        #endregion

        #region Tsvrc Callbacks
        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            _readyPlayerIds = new string[0];
            AddTrackedPlayers(_tempPlayerIds);
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastCheckStarted), _tempPlayerIds);

            _tempPlayerIds = new string[0];
        }

        protected override void OnProcessCompleted()
        {
            base.OnProcessCompleted();
            // Store the ready player IDs to send with the completion event
            var playerIds = _tempPlayerIds;

            _readyPlayerIds = new string[0];
            _tempPlayerIds = new string[0];
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastCheckCompleted), playerIds);
        }

        protected override void OnProcessCancelled()
        {
            base.OnProcessCancelled();

            _readyPlayerIds = new string[0];
            _tempPlayerIds = new string[0];
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastCheckCancelled));
        }

        protected override void OnTrackedPlayersRemoved(VRCPlayerApi[] players)
        {
            base.OnTrackedPlayersRemoved(players);

            foreach (VRCPlayerApi player in players)
            {
                string playerId = TsPlayerUtils.GetPlayerID(player);
                if (IsPlayerReady(playerId))
                {
                    SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RemoveReadyPlayerEvent), playerId);
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts a ready check for the specified players.
        /// </summary>
        public void StartReadyCheck(string[] playerIds)
        {
            _tempPlayerIds = playerIds;
            StartProcess();
        }

        /// <summary>
        /// Cancels the current ready check.
        /// </summary>
        public void CancelReadyCheck()
        {
            CancelProcess();
        }

        /// <summary>
        /// Set the local player's ready status.
        /// </summary>
        public void SetReady(bool ready = true)
        {
            if (!IsRunning()) return;

            string playerId = TsPlayerUtils.GetPlayerID(Networking.LocalPlayer);

            if (ready)
            {
                if (IsPlayerReady(playerId)) return;
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(AddReadyPlayerEvent), playerId);
            }
            else
            {
                if (!IsPlayerReady(playerId)) return;
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RemoveReadyPlayerEvent), playerId);
            }
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Checks if a player is marked as ready.
        /// </summary>
        protected bool IsPlayerReady(string playerId)
        {
            return TsArray.Contains(_readyPlayerIds, playerId);
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when a ready check is started.
        /// This method is intended to be called on all clients.
        /// Make sure to invoked base.OnReadyCheckStarted if overridden.
        /// </summary>
        protected virtual void OnReadyCheckStarted(string[] expectedPlayerIds) { }

        /// <summary>
        /// Called when a ready check is completed successfully.
        /// This method is intended to be called on all clients.
        /// Make sure to invoked base.OnReadyCheckCompleted if overridden.
        /// </summary>
        protected virtual void OnReadyCheckCompleted(string[] playerIds) { }

        /// <summary>
        /// Called when a ready check is cancelled.
        /// This method is intended to be called on all clients.
        /// Make sure to invoked base.OnReadyCheckCancelled if overridden.
        /// </summary>
        protected virtual void OnReadyCheckCancelled() { }

        #endregion

        #region Network Events

        /// <summary>
        /// Adds a player to the ready list.
        /// This method is network callable and is intended to be called on the owner.
        /// </summary>
        [NetworkCallable]
        public void AddReadyPlayerEvent(string playerId)
        {
            string[] playerIds = new string[1];
            playerIds[0] = playerId;
            _readyPlayerIds = TsArray.Add(_readyPlayerIds, playerIds);
            RequestSerialization();
        }

        /// <summary>
        /// Removes a player from the ready list.
        /// This method is network callable and is intended to be called on the owner.
        /// </summary>
        [NetworkCallable]
        public void RemoveReadyPlayerEvent(string playerId)
        {
            string[] playerIds = new string[1];
            playerIds[0] = playerId;
            _readyPlayerIds = TsArray.Remove(_readyPlayerIds, playerIds);
            RequestSerialization();
        }

        /// <summary>
        /// Network callable event to notify all the players that a ready check has started.
        /// </summary>
        [NetworkCallable]
        public void BroadcastCheckStarted(string[] expectedPlayerIds)
        {
            OnReadyCheckStarted(expectedPlayerIds);
        }

        /// <summary>
        /// Network callable event to notify all clients that the ready check has been completed.
        /// </summary>
        [NetworkCallable]
        public void BroadcastCheckCompleted(string[] playerIds)
        {
            OnReadyCheckCompleted(playerIds);
        }

        /// <summary>
        /// Network callable event to notify all clients that the ready check has been cancelled.
        /// </summary>
        [NetworkCallable]
        public void BroadcastCheckCancelled()
        {
            OnReadyCheckCancelled();
        }

        #endregion

        #region Private Methods
        #endregion
    }
}
