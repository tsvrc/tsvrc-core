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

        private UdonSharpBehaviour _readyCheckListener;
        private string _onReadyCheckStartedEvent = "OnReadyCheckStarted";
        private string _onReadyCheckStoppedEvent = "OnReadyCheckStopped";
        private string _onReadyCheckCompletedEvent = "OnReadyCheckCompleted";

        /// <summary>
        /// Initializes the TsReadyCheckProcess with a listener and event method names.
        /// On each ready check event, SendCustomEvent is called on the listener using the corresponding name.
        /// Use nameof() for event names to avoid magic strings and get refactor safety.
        /// Read event data from the Last* properties inside the listener's callback methods:
        /// <list type="bullet">
        /// <item><term>onReadyCheckStartedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onReadyCheckStoppedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onReadyCheckCompletedEvent</term><description>LastPlayerIds</description></item>
        /// </list>
        /// <example>
        /// <code>
        /// readyCheck.TsConstruct(
        ///     this,
        ///     nameof(OnReadyCheckStartedMethod),
        ///     nameof(OnReadyCheckStoppedMethod),
        ///     nameof(OnReadyCheckCompletedMethod)
        /// );
        ///
        /// public void OnReadyCheckCompletedMethod()
        /// {
        ///     var players = readyCheck.LastPlayerIds;
        /// }
        /// </code>
        /// </example>
        /// </summary>
        public void TsConstruct(
            UdonSharpBehaviour listener,
            string onReadyCheckStartedEvent,
            string onReadyCheckStoppedEvent,
            string onReadyCheckCompletedEvent
        )
        {
            _readyCheckListener = listener;
            _onReadyCheckStartedEvent = onReadyCheckStartedEvent;
            _onReadyCheckStoppedEvent = onReadyCheckStoppedEvent;
            _onReadyCheckCompletedEvent = onReadyCheckCompletedEvent;

            base.TsConstruct(
                this,
                nameof(_OnTrackingStarted),
                nameof(_OnTrackingStopped),
                nameof(_OnTrackingCompleted),
                nameof(_OnTrackingDeserialization),
                nameof(_OnTrackingPlayersAdded),
                nameof(_OnTrackingPlayersRemoved)
            );
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
            _readyCheckListener.SendCustomEvent(_onReadyCheckStartedEvent);
        }

        public void _OnTrackingStopped()
        {
            _readyCheckListener.SendCustomEvent(_onReadyCheckStoppedEvent);
        }

        public void _OnTrackingCompleted()
        {
            _readyCheckListener.SendCustomEvent(_onReadyCheckCompletedEvent);
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
