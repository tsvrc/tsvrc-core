using Tsvrc.Core;
using Tsvrc.Player;
using Tsvrc.Utils;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsPlayerTracker : TsvrcProcess
    {
        [UdonSynced] private string[] _trackedPlayerIds = new string[0];

        // Temporary storage for initial player IDs during process start.
        protected string[] _initialTrackerPlayerIds = new string[0];

        private UdonSharpBehaviour _listener;
        private string _onTrackingStartedEvent = "OnTrackingStarted";
        private string _onTrackingStoppedEvent = "OnTrackingStopped";
        private string _onTrackingCompletedEvent = "OnTrackingCompleted";
        private string _onTrackingDeserializationEvent = "OnTrackingDeserialization";
        private string _onTrackingPlayersAddedEvent = "OnTrackingPlayersAdded";
        private string _onTrackingPlayersRemovedEvent = "OnTrackingPlayersRemoved";

        public string[] LastPlayerIds { get; private set; } = new string[0];
        public string[] LastAddedPlayerIds { get; private set; } = new string[0];
        public string[] LastRemovedPlayerIds { get; private set; } = new string[0];

        /// <summary>
        /// Initializes the TsPlayerTracker with a listener and event method names.
        /// On each tracker event, SendCustomEvent is called on the listener using the corresponding name.
        /// Use nameof() for event names to avoid magic strings and get refactor safety.
        /// Read event data from the Last* properties inside the listener's callback methods:
        /// <list type="bullet">
        /// <item><term>onTrackingStartedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onTrackingStoppedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onTrackingCompletedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onTrackingDeserializationEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onTrackingPlayersAddedEvent</term><description>LastAddedPlayerIds</description></item>
        /// <item><term>onTrackingPlayersRemovedEvent</term><description>LastRemovedPlayerIds</description></item>
        /// </list>
        /// <example>
        /// <code>
        /// tracker.TsConstruct(
        ///     this,
        ///     nameof(OnTrackingStartedMethod),
        ///     nameof(OnTrackingStoppedMethod),
        ///     nameof(OnTrackingCompletedMethod),
        ///     nameof(OnTrackingDeserializationMethod),
        ///     nameof(OnTrackingPlayersAddedMethod),
        ///     nameof(OnTrackingPlayersRemovedMethod)
        /// );
        ///
        /// public void OnTrackingPlayersAddedMethod()
        /// {
        ///     var added = tracker.LastAddedPlayerIds;
        /// }
        /// </code>
        /// </example>
        /// </summary>
        public void TsConstruct(
            UdonSharpBehaviour listener,
            string onTrackingStartedEvent,
            string onTrackingStoppedEvent,
            string onTrackingCompletedEvent,
            string onTrackingDeserializationEvent,
            string onTrackingPlayersAddedEvent,
            string onTrackingPlayersRemovedEvent
        )
        {
            _listener = listener;
            _onTrackingStartedEvent = onTrackingStartedEvent;
            _onTrackingStoppedEvent = onTrackingStoppedEvent;
            _onTrackingCompletedEvent = onTrackingCompletedEvent;
            _onTrackingDeserializationEvent = onTrackingDeserializationEvent;
            _onTrackingPlayersAddedEvent = onTrackingPlayersAddedEvent;
            _onTrackingPlayersRemovedEvent = onTrackingPlayersRemovedEvent;
        }

        #region VRChat Callbacks

        public override void OnDeserialization()
        {
            LastPlayerIds = _trackedPlayerIds;
            _listener.SendCustomEvent(_onTrackingDeserializationEvent);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            base.OnPlayerLeft(player);

            if (!IsProcessRunning() || !IsProcessOwner()) return;

            var playerId = TsPlayer.GetPlayerID(player);
            var players = TsPlayer.ToArray(playerId);
            RemoveTrackedPlayers(players);
        }

        #endregion

        #region TsvrcProcess Callbacks

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            _trackedPlayerIds = _initialTrackerPlayerIds;
            _initialTrackerPlayerIds = new string[0];
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersProcessStarted), _trackedPlayerIds);
        }

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();

            var stoppedPlayerIds = (string[])_trackedPlayerIds.Clone();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersProcessStopped), stoppedPlayerIds);
        }

        protected override void OnProcessCompleted()
        {
            base.OnProcessCompleted();

            var newPlayerIds = (string[])_trackedPlayerIds.Clone();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersProcessCompleted), newPlayerIds);
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            _trackedPlayerIds = new string[0];
            RequestSerialization();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the process from the tracker with the specified player IDs.
        /// </summary>
        public virtual void StartPlayerTracking(string[] playerIds, bool useProcessUpdate = false)
        {
            _initialTrackerPlayerIds = playerIds;

            base.StartProcess(useProcessUpdate);
        }

        /// <summary>
        /// Stops the process from the tracker before completion.
        /// </summary>
        public virtual void StopPlayerTracking()
        {
            base.StopProcess();
        }

        /// <summary>
        /// Completes the process from the tracker.
        /// </summary>
        public virtual void CompletePlayerTracking()
        {
            base.CompleteProcess();
        }

        /// <summary>
        /// Adds multiple players to the tracked list.
        /// </summary>
        public void AddTrackedPlayers(string[] playerIds)
        {
            if (playerIds == null || playerIds.Length == 0)
            {
                Debug.LogWarning("AddTrackedPlayers called with null or empty array");
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(BroadcastAddTrackedPlayers), playerIds);
        }

        /// <summary>
        /// Removes multiple players from the tracked list.
        /// </summary>
        public void RemoveTrackedPlayers(string[] playerIds)
        {
            if (playerIds == null || playerIds.Length == 0 || _trackedPlayerIds.Length == 0)
            {
                Debug.LogWarning("RemoveTrackedPlayers called with null or empty array");
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(BroadcastRemoveTrackedPlayers), playerIds);
        }
        #endregion

        #region Protected Methods

        /// <summary>
        /// Gets the list of currently tracked player IDs.
        /// </summary>
        protected string[] GetTrackedPlayerIds()
        {
            return _trackedPlayerIds;
        }

        /// <summary>
        /// Checks if a player with the given ID is being tracked.
        /// </summary>
        protected bool IsTrackedPlayer(string playerId)
        {
            return TsArray.Contains(_trackedPlayerIds, playerId);
        }

        #endregion

        #region  Network Events

        /// <summary>
        /// Network callable method to notify tracked players that the process has started.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessStarted(string[] playerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(playerIds, playerId)) return;

            LastPlayerIds = playerIds;
            _listener.SendCustomEvent(_onTrackingStartedEvent);
        }

        /// <summary>
        /// Network callable method to notify tracked players that the process has stopped.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessStopped(string[] playerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(playerIds, playerId)) return;

            LastPlayerIds = playerIds;
            _listener.SendCustomEvent(_onTrackingStoppedEvent);
        }

        /// <summary>
        /// Network callable method to notify tracked players that the process has completed.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessCompleted(string[] playerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(playerIds, playerId)) return;

            LastPlayerIds = playerIds;
            _listener.SendCustomEvent(_onTrackingCompletedEvent);
        }

        /// <summary>
        /// Network callable method to notify tracked players that new players were added.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersAdded(string[] addedPlayerIds, string[] notifyPlayerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(notifyPlayerIds, playerId)) return;

            LastAddedPlayerIds = addedPlayerIds;
            _listener.SendCustomEvent(_onTrackingPlayersAddedEvent);
        }

        /// <summary>
        /// Network callable method to notify tracked players that players were removed.
        /// This method is invoked on all players via network event, but only executes for remaining tracked players.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersRemoved(string[] removedPlayerIds, string[] notifyPlayerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(notifyPlayerIds, playerId)) return;

            LastRemovedPlayerIds = removedPlayerIds;
            _listener.SendCustomEvent(_onTrackingPlayersRemovedEvent);
        }

        /// <summary>
        /// Adds players to the tracked list.
        /// This method is network callable and is intended to be called on the owner.
        /// </summary>
        [NetworkCallable]
        public void BroadcastAddTrackedPlayers(string[] playerIds)
        {
            // Filter out already tracked players
            string[] validPlayerIds = new string[playerIds.Length];
            int validCount = 0;

            for (int i = 0; i < playerIds.Length; i++)
            {
                if (!IsTrackedPlayer(playerIds[i]))
                {
                    validPlayerIds[validCount++] = playerIds[i];
                }
            }

            if (validCount == 0) return;

            // Trim to actual count
            if (validCount < playerIds.Length)
            {
                string[] trimmed = new string[validCount];
                System.Array.Copy(validPlayerIds, trimmed, validCount);
                validPlayerIds = trimmed;
            }

            // Save current tracked players before adding new ones
            var currentTrackedPlayers = (string[])_trackedPlayerIds.Clone();

            _trackedPlayerIds = TsArray.Add(_trackedPlayerIds, validPlayerIds);
            RequestSerialization();

            // Notify current tracked players about new additions
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersAdded), validPlayerIds, currentTrackedPlayers);
        }

        /// <summary>
        /// Removes players from the tracked list.
        /// This method is network callable and is intended to be called on the owner.
        /// </summary>
        [NetworkCallable]
        public void BroadcastRemoveTrackedPlayers(string[] playerIds)
        {
            // Filter to only tracked players
            string[] validPlayerIds = new string[playerIds.Length];
            int validCount = 0;

            for (int i = 0; i < playerIds.Length; i++)
            {
                if (IsTrackedPlayer(playerIds[i]))
                {
                    validPlayerIds[validCount++] = playerIds[i];
                }
            }

            if (validCount == 0) return;

            // Trim to actual count
            if (validCount < playerIds.Length)
            {
                string[] trimmed = new string[validCount];
                System.Array.Copy(validPlayerIds, trimmed, validCount);
                validPlayerIds = trimmed;
            }

            // Determine who to notify (all tracked players except those being removed)
            var notifyPlayerIds = TsArray.Remove(_trackedPlayerIds, validPlayerIds);

            _trackedPlayerIds = TsArray.Remove(_trackedPlayerIds, validPlayerIds);
            RequestSerialization();

            // Notify remaining tracked players about removals
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersRemoved), validPlayerIds, notifyPlayerIds);
        }

        #endregion
    }
}