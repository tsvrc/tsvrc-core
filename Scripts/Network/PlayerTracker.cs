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
            OnTrackingDeserialization();
            TsEmit(OnTrackingDeserializationEvent);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            base.OnPlayerLeft(player);

            if (!IsProcessRunning() || !IsProcessOwner()) return;

            var playerId = TsPlayer.GetPlayerID(player);
            if (!IsTrackedPlayer(playerId)) return;
            // Already confirmed as the process owner, so call directly to avoid the self-loop
            // overhead of SendCustomNetworkEvent(Owner,...) firing back to us synchronously.
            BroadcastRemoveTrackedPlayers(TsPlayer.ToArray(playerId));
        }

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            _trackedPlayerIds = _initialTrackerPlayerIds;
            _initialTrackerPlayerIds = new string[0];
            // base.StartProcess() serialized before OnProcessStarted was called, so _trackedPlayerIds
            // was still empty at that point. Serialize now so late joiners receive the correct list.
            RequestSerialization();

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
            //   IsTrackedPlayer() is false in PlayerTracker.OnPlayerLeft, so no double removal.
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

            // De-dup the initial list to maintain the same no-duplicate invariant that
            // BroadcastAddTrackedPlayers enforces at runtime via IsTrackedPlayer checks.
            // Without this, a caller passing repeated IDs would produce duplicates in
            // _trackedPlayerIds, corrupting LastPlayerIds on all clients and causing
            // OnOwnerAbandonedProcess to broadcast spurious double-entries in the removed list.
            if (playerIds.Length > 1)
            {
                string[] deduped = new string[playerIds.Length];
                int dedupedCount = 0;
                for (int i = 0; i < playerIds.Length; i++)
                {
                    bool isDuplicate = false;
                    for (int j = 0; j < dedupedCount; j++)
                    {
                        if (deduped[j] == playerIds[i]) { isDuplicate = true; break; }
                    }
                    if (!isDuplicate)
                        deduped[dedupedCount++] = playerIds[i];
                }
                if (dedupedCount < playerIds.Length)
                {
                    string[] trimmed = new string[dedupedCount];
                    System.Array.Copy(deduped, trimmed, dedupedCount);
                    playerIds = trimmed;
                }
            }

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
        /// On non-owner clients this reflects the last deserialized state, which may lag behind
        /// network event callbacks. Use <c>LastPlayerIds</c> for an accurate snapshot within <c>Notify*</c> callbacks.
        /// </summary>
        protected bool IsTrackedPlayer(string playerId)
        {
            return TsArray.Contains(_trackedPlayerIds, playerId);
        }

        /// <summary>Called on all clients when tracking starts. Read <see cref="LastPlayerIds"/> in this callback.</summary>
        /// <param name="playerIds">The initial set of tracked player IDs.</param>
        protected virtual void OnTrackingStarted(string[] playerIds) { }

        /// <summary>Called on all clients when tracking stops. Read <see cref="LastPlayerIds"/> in this callback.</summary>
        /// <param name="playerIds">The tracked player IDs at the time of stopping.</param>
        protected virtual void OnTrackingStopped(string[] playerIds) { }

        /// <summary>Called on all clients when tracking completes. Read <see cref="LastPlayerIds"/> in this callback.</summary>
        /// <param name="playerIds">The tracked player IDs at the time of completion.</param>
        protected virtual void OnTrackingCompleted(string[] playerIds) { }

        /// <summary>Called on non-owner clients when synced state is received. Read <see cref="LastPlayerIds"/> in this callback.</summary>
        protected virtual void OnTrackingDeserialization() { }

        /// <summary>Called on all clients when players are added. Read <see cref="LastAddedPlayerIds"/> in this callback.</summary>
        /// <param name="addedPlayerIds">The player IDs that were added.</param>
        protected virtual void OnTrackingPlayersAdded(string[] addedPlayerIds) { }

        /// <summary>Called on all clients when players are removed. Read <see cref="LastRemovedPlayerIds"/> in this callback.</summary>
        /// <param name="removedPlayerIds">The player IDs that were removed.</param>
        protected virtual void OnTrackingPlayersRemoved(string[] removedPlayerIds) { }

        /// <summary>
        /// Broadcast target: fires on all instance players when the process starts.
        /// Read <c>LastPlayerIds</c> to check if the local player is tracked.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessStarted(string[] playerIds)
        {
            // Null guard: any [NetworkCallable] method can be called by any player in the
            // instance with null parameters (VRChat passes default(T), which is null for arrays).
            // LastPlayerIds = null would crash subscribers doing LastPlayerIds.Length.
            if (playerIds == null) return;
            LastPlayerIds = playerIds;
            OnTrackingStarted(playerIds);
            TsEmit(OnTrackingStartedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the process stops.
        /// Read <c>LastPlayerIds</c> to check if the local player is tracked.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessStopped(string[] playerIds)
        {
            if (playerIds == null) return;
            LastPlayerIds = playerIds;
            OnTrackingStopped(playerIds);
            TsEmit(OnTrackingStoppedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the process completes.
        /// Read <c>LastPlayerIds</c> to check if the local player is tracked.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersProcessCompleted(string[] playerIds)
        {
            if (playerIds == null) return;
            LastPlayerIds = playerIds;
            OnTrackingCompleted(playerIds);
            TsEmit(OnTrackingCompletedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when players are added.
        /// Read <c>LastAddedPlayerIds</c> in your callback.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersAdded(string[] addedPlayerIds)
        {
            if (addedPlayerIds == null) return;
            LastAddedPlayerIds = addedPlayerIds;
            // Apply the delta so LastPlayerIds is current when the callback runs.
            // On the owner _trackedPlayerIds is already updated before this fires (synchronous).
            // On remotes the serialization packet may arrive BEFORE this event (no relative
            // ordering guarantee between RequestSerialization and SendCustomNetworkEvent per
            // VRChat docs), so LastPlayerIds may already contain some/all of addedPlayerIds.
            // Guard against duplicates by mirroring the de-dup pattern in BroadcastAddTrackedPlayers.
            string[] toAdd = new string[addedPlayerIds.Length];
            int toAddCount = 0;
            for (int i = 0; i < addedPlayerIds.Length; i++)
            {
                if (!TsArray.Contains(LastPlayerIds, addedPlayerIds[i]))
                    toAdd[toAddCount++] = addedPlayerIds[i];
            }
            if (toAddCount > 0)
            {
                if (toAddCount < toAdd.Length)
                {
                    string[] trimmed = new string[toAddCount];
                    System.Array.Copy(toAdd, trimmed, toAddCount);
                    toAdd = trimmed;
                }
                LastPlayerIds = TsArray.Add(LastPlayerIds, toAdd);
            }
            OnTrackingPlayersAdded(addedPlayerIds);
            TsEmit(OnTrackingPlayersAddedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when players are removed.
        /// Read <c>LastRemovedPlayerIds</c> in your callback.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersRemoved(string[] removedPlayerIds)
        {
            // Null guard: same rationale as NotifyTrackedPlayersProcessStarted.
            // TsArray.Remove would NullReferenceException on items.Length if null is passed.
            if (removedPlayerIds == null) return;
            LastRemovedPlayerIds = removedPlayerIds;
            // Apply the delta so LastPlayerIds is current when the callback runs.
            // Mirrors the delta applied in NotifyTrackedPlayersAdded.
            LastPlayerIds = TsArray.Remove(LastPlayerIds, removedPlayerIds);
            OnTrackingPlayersRemoved(removedPlayerIds);
            TsEmit(OnTrackingPlayersRemovedEvent);
        }

        /// <summary>
        /// Adds players to the tracked list. Silently ignores IDs already tracked.
        /// Non-owners should call <see cref="AddTrackedPlayers"/> instead.
        /// </summary>
        [NetworkCallable]
        public void BroadcastAddTrackedPlayers(string[] playerIds)
        {
            // IsProcessRunning() guards the window between ExecuteStop setting _isRunning=false
            // and InternalCleanup clearing _ownerId. During that window IsProcessOwner() is still
            // true, so a subscriber callback from OnProcessStopped/OnProcessCompleted that calls
            // AddTrackedPlayers would pass the IsProcessOwner() check alone and mutate
            // _trackedPlayerIds + send a spurious NotifyTrackedPlayersAdded event to all clients.
            if (!IsProcessRunning() || !IsProcessOwner()) return;
            if (playerIds == null || playerIds.Length == 0) return;

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
            // Same guard rationale as BroadcastAddTrackedPlayers; see its comment.
            if (!IsProcessRunning() || !IsProcessOwner()) return;
            if (playerIds == null || playerIds.Length == 0) return;

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