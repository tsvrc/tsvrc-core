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
    /// <summary>
    /// A <see cref="TsvrcProcess"/> that tracks a set of players by ID.
    /// The owner manages the list; all clients receive network events when the set changes.
    /// Subscribe via the <c>OnTracking*Event</c> string constants and read the <c>Last*</c> properties in your callback.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PlayerTracker : TsvrcProcess
    {
        /// <summary>
        /// Emitted when tracking starts.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTrackingStartedEvent = "OnTrackingStarted";
        /// <summary>
        /// Emitted when tracking stops.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTrackingStoppedEvent = "OnTrackingStopped";
        /// <summary>
        /// Emitted when tracking completes.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTrackingCompletedEvent = "OnTrackingCompleted";
        /// <summary>
        /// Emitted on deserialization.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTrackingDeserializationEvent = "OnTrackingDeserialization";
        /// <summary>
        /// Emitted when players are added.
        /// Read <c>LastAddedPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTrackingPlayersAddedEvent = "OnTrackingPlayersAdded";
        /// <summary>
        /// Emitted when players are removed.
        /// Read <c>LastRemovedPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTrackingPlayersRemovedEvent = "OnTrackingPlayersRemoved";

        [UdonSynced] private string[] _trackedPlayerIds = new string[0];

        // Temporary storage for initial player IDs during process start.
        protected string[] _initialTrackerPlayerIds = new string[0];

        /// <summary>The tracked player IDs at the time of the last received broadcast or deserialization.</summary>
        public string[] LastPlayerIds { get; private set; } = new string[0];
        /// <summary>The player IDs added in the last <see cref="BroadcastAddTrackedPlayers"/> broadcast.</summary>
        public string[] LastAddedPlayerIds { get; private set; } = new string[0];
        /// <summary>The player IDs removed in the last <see cref="BroadcastRemoveTrackedPlayers"/> broadcast.</summary>
        public string[] LastRemovedPlayerIds { get; private set; } = new string[0];

        public override void OnDeserialization()
        {
            base.OnDeserialization();

            LastPlayerIds = _trackedPlayerIds;
            TsEmit(OnTrackingDeserializationEvent);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            base.OnPlayerLeft(player);

            if (!IsProcessRunning() || !IsProcessOwner()) return;

            var playerId = TsPlayer.GetPlayerID(player);
            if (!IsTrackedPlayer(playerId)) return;
            // Already confirmed as the process owner — call directly to avoid the self-loop
            // overhead of SendCustomNetworkEvent(Owner,...) firing back to us synchronously.
            BroadcastRemoveTrackedPlayers(TsPlayer.ToArray(playerId));
        }

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            _trackedPlayerIds = _initialTrackerPlayerIds;
            _initialTrackerPlayerIds = new string[0];

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersProcessStarted), _trackedPlayerIds);
        }

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersProcessStopped), _trackedPlayerIds);
        }

        protected override void OnProcessCompleted()
        {
            base.OnProcessCompleted();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersProcessCompleted), _trackedPlayerIds);
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            _trackedPlayerIds = new string[0];
            _initialTrackerPlayerIds = new string[0];
            LastPlayerIds = new string[0];
            LastAddedPlayerIds = new string[0];
            LastRemovedPlayerIds = new string[0];
        }

        protected override void OnOwnerAbandonedProcess()
        {
            base.OnOwnerAbandonedProcess();

            // Handles both event-ordering cases when a tracked process owner leaves:
            // - Normal flow: base.OnPlayerLeft ran TakeOverAbandonedProcess; after returning,
            //   IsTrackedPlayer() is false in PlayerTracker.OnPlayerLeft — no double removal.
            // - VRChat bug flow: OnPlayerLeft found IsProcessOwner()=false and skipped removal;
            //   this scan catches it via the OnOwnershipTransferred fallback path.
            // GetAllPlayerIDs() is called once rather than per-entry to avoid repeated SDK allocation.
            if (_trackedPlayerIds.Length == 0) return;

            string[] currentPlayerIds = TsPlayer.GetAllPlayerIDs();
            string[] departed = new string[_trackedPlayerIds.Length];
            int departedCount = 0;
            for (int i = 0; i < _trackedPlayerIds.Length; i++)
            {
                if (!TsArray.Contains(currentPlayerIds, _trackedPlayerIds[i]))
                    departed[departedCount++] = _trackedPlayerIds[i];
            }

            if (departedCount == 0) return;

            if (departedCount < departed.Length)
            {
                string[] trimmed = new string[departedCount];
                System.Array.Copy(departed, trimmed, departedCount);
                departed = trimmed;
            }

            BroadcastRemoveTrackedPlayers(departed);
        }

        /// <summary>
        /// Starts the process from the tracker with the specified player IDs.
        /// </summary>
        /// <param name="useProcessUpdate">When <c>true</c>, <see cref="OnProcessUpdate"/> fires every 0.5 s while the process runs.</param>
        public virtual void StartPlayerTracking(string[] playerIds, bool useProcessUpdate = false)
        {
            if (playerIds == null) playerIds = new string[0];
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

            // Owner fast-path: avoid the self-loop overhead of SendCustomNetworkEvent(Owner,...).
            if (IsProcessOwner())
            {
                BroadcastAddTrackedPlayers(playerIds);
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(BroadcastAddTrackedPlayers), playerIds);
        }

        /// <summary>
        /// Removes multiple players from the tracked list.
        /// </summary>
        public void RemoveTrackedPlayers(string[] playerIds)
        {
            if (playerIds == null || playerIds.Length == 0)
            {
                Debug.LogWarning("RemoveTrackedPlayers called with null or empty array");
                return;
            }

            // Owner fast-path: avoid the self-loop overhead of SendCustomNetworkEvent(Owner,...).
            if (IsProcessOwner())
            {
                BroadcastRemoveTrackedPlayers(playerIds);
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(BroadcastRemoveTrackedPlayers), playerIds);
        }

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

        /// <summary>
        /// Broadcast target: fires on all instance players when the process starts.
        /// Read <c>LastPlayerIds</c> or call <see cref="IsTrackedPlayer"/> to check if the local player is tracked.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessStarted(string[] playerIds)
        {
            LastPlayerIds = playerIds;
            TsEmit(OnTrackingStartedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the process stops.
        /// Read <c>LastPlayerIds</c> or call <see cref="IsTrackedPlayer"/> to check if the local player is tracked.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessStopped(string[] playerIds)
        {
            LastPlayerIds = playerIds;
            TsEmit(OnTrackingStoppedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the process completes.
        /// Read <c>LastPlayerIds</c> or call <see cref="IsTrackedPlayer"/> to check if the local player is tracked.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessCompleted(string[] playerIds)
        {
            LastPlayerIds = playerIds;
            TsEmit(OnTrackingCompletedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when players are added.
        /// Read <c>LastAddedPlayerIds</c> in your callback.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersAdded(string[] addedPlayerIds)
        {
            LastAddedPlayerIds = addedPlayerIds;
            TsEmit(OnTrackingPlayersAddedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when players are removed.
        /// Read <c>LastRemovedPlayerIds</c> in your callback.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersRemoved(string[] removedPlayerIds)
        {
            LastRemovedPlayerIds = removedPlayerIds;
            TsEmit(OnTrackingPlayersRemovedEvent);
        }

        /// <summary>
        /// Adds players to the tracked list. Silently ignores IDs already tracked.
        /// Non-owners should call <see cref="AddTrackedPlayers"/> instead.
        /// </summary>
        [NetworkCallable]
        public void BroadcastAddTrackedPlayers(string[] playerIds)
        {
            if (!IsProcessOwner()) return;

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

            if (validCount < playerIds.Length)
            {
                string[] trimmed = new string[validCount];
                System.Array.Copy(validPlayerIds, trimmed, validCount);
                validPlayerIds = trimmed;
            }

            _trackedPlayerIds = TsArray.Add(_trackedPlayerIds, validPlayerIds);
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersAdded), validPlayerIds);
        }

        /// <summary>
        /// Removes players from the tracked list. Silently ignores IDs not currently tracked.
        /// Non-owners should call <see cref="RemoveTrackedPlayers"/> instead.
        /// </summary>
        [NetworkCallable]
        public void BroadcastRemoveTrackedPlayers(string[] playerIds)
        {
            if (!IsProcessOwner()) return;

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

            if (validCount < playerIds.Length)
            {
                string[] trimmed = new string[validCount];
                System.Array.Copy(validPlayerIds, trimmed, validCount);
                validPlayerIds = trimmed;
            }

            var remainingPlayerIds = TsArray.Remove(_trackedPlayerIds, validPlayerIds);

            _trackedPlayerIds = remainingPlayerIds;
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersRemoved), validPlayerIds);
        }
    }
}