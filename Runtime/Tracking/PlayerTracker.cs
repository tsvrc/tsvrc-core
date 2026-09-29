using Tsvrc.Core;
using Tsvrc.Player;
using Tsvrc.Utils;
using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Tracking
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [TsWorldExtensionPoint("TsPlayerTracker")]
    public class PlayerTracker : Process
    {
        public const string OnTrackingStartedEvent = "OnTrackingStarted";
        public const string OnTrackingStoppedEvent = "OnTrackingStopped";
        public const string OnTrackingCompletedEvent = "OnTrackingCompleted";
        public const string OnTrackingPlayersAddedEvent = "OnTrackingPlayersAdded";
        public const string OnTrackingPlayersRemovedEvent = "OnTrackingPlayersRemoved";

        [UdonSynced] private int[] _trackedPlayerIds = new int[0];

        public int[] TrackedPlayerIds
        {
            get => _trackedPlayerIds;
        }

        public string[] LastAddedPlayerIds { get; private set; } = new string[0];
        public string[] LastRemovedPlayerIds { get; private set; } = new string[0];

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            base.OnPlayerLeft(player);

            if (!IsProcessRunning() || !IsProcessOwner()) return;

            var playerId = player.playerId;
            if (!IsTrackedPlayer(playerId)) return;
            RequestRemoveTrackedPlayers(playerId);
        }

        public override void OnPlayerSuspendChanged(VRCPlayerApi player)
        {
            base.OnPlayerSuspendChanged(player);

            if (!player.isSuspended || !IsProcessRunning() || !IsProcessOwner()) return;

            var playerId = player.playerId;
            if (!IsTrackedPlayer(playerId)) return;
            RequestRemoveTrackedPlayers(playerId);
        }

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

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

            if (IsProcessRunning()) return;

            _trackedPlayerIds = new int[0];
            LastAddedPlayerIds = new string[0];
            LastRemovedPlayerIds = new string[0];
        }

        protected override void OnBecameProcessOwner()
        {
            base.OnBecameProcessOwner();

            if (_trackedPlayerIds.Length == 0) return;

            VRCPlayerApi[] allPlayers = TsPlayer.GetAllPlayers();
            string[] activePlayers = new string[allPlayers.Length];
            int activeCount = 0;
            for (int i = 0; i < allPlayers.Length; i++)
            {
                if (!allPlayers[i].isSuspended)
                    activePlayers[activeCount++] = TsPlayer.GetPlayerID(allPlayers[i]);
            }
            if (activeCount < allPlayers.Length)
            {
                string[] trimmed = new string[activeCount];
                System.Array.Copy(activePlayers, trimmed, activeCount);
                activePlayers = trimmed;
            }

            string[] toRemove = new string[_trackedPlayerIds.Length];
            int removeCount = 0;
            for (int i = 0; i < _trackedPlayerIds.Length; i++)
            {
                if (!TsArray.Contains(activePlayers, _trackedPlayerIds[i]))
                    toRemove[removeCount++] = _trackedPlayerIds[i];
            }

            if (removeCount == 0) return;

            if (removeCount < toRemove.Length)
            {
                string[] trimmed = new string[removeCount];
                System.Array.Copy(toRemove, trimmed, removeCount);
                toRemove = trimmed;
            }

            RequestRemoveTrackedPlayers(toRemove);
        }

        public virtual void StartPlayerTracking(int[] playerIds)
        {
            base.StartProcess();
        }

        protected override void ExecuteProcessStart()
        {
            if (playerIds == null) playerIds = new string[0];

            playerIds = TsArray.Remove(playerIds, new string[] { null });

            playerIds = TsArray.Dedupe(playerIds);

            _initialTrackerPlayerIds = playerIds;

            base.ExecuteProcessStart();
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
                LogWarning("AddTrackedPlayers called with null or empty array");
                return;
            }

            // Owner fast path: avoid the self loop overhead of SendCustomNetworkEvent(Owner,...).
            if (IsProcessOwner())
            {
                RequestAddTrackedPlayers(playerIds);
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestAddTrackedPlayers), playerIds);
        }

        /// <summary>
        /// Removes multiple players from the tracked list.
        /// </summary>
        public void RemoveTrackedPlayers(string[] playerIds)
        {
            if (playerIds == null || playerIds.Length == 0)
            {
                LogWarning("RemoveTrackedPlayers called with null or empty array");
                return;
            }

            // Owner fast path: avoid the self loop overhead of SendCustomNetworkEvent(Owner,...).
            if (IsProcessOwner())
            {
                RequestRemoveTrackedPlayers(playerIds);
                return;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestRemoveTrackedPlayers), playerIds);
        }

        /// <summary>
        /// Returns true if <paramref name="playerId"/> is in this player's copy of the synced
        /// tracked list.
        /// </summary>
        protected bool IsTrackedPlayer(int playerId)
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
        // maxEventsPerSecond: 100. VRChat only guarantees event ordering from a single sender
        // when neither event type hits its rate limit. NotifyTrackedPlayersAdded/Removed also
        // use 100/s; keeping all owner-broadcast events at the same limit ensures that rapid
        // add/remove sequences cannot skip ahead of or fall behind start/stop/complete events
        // on remote clients, so LastPlayerIds is always current when lifecycle callbacks fire.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersProcessStarted(string[] playerIds)
        {
            // Any [NetworkCallable] can be invoked with null parameters from the network;
            // LastPlayerIds = null would crash any subscriber reading LastPlayerIds.Length.
            if (playerIds == null) return;
            // Only the process owner sends this event. Any instance player can invoke a
            // [NetworkCallable] method directly; without this guard a malicious player could
            // corrupt LastPlayerIds and trigger false lifecycle events on all clients.
            // _isBroadcasting bypasses the check during the owner's own inline execution
            // where CallingPlayer may be propagated from an outer event context.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Sanitize independently of the sender, same rationale/pattern as
            // NotifyTrackedPlayersAdded/Removed: this method is itself public and
            // [NetworkCallable], so a direct call can carry nulls/duplicates the normal
            // OnProcessStarted->_trackedPlayerIds path would never produce. Unlike
            // Added/Removed, an empty (but non-null) result is a legitimate value here (a
            // tracker started with zero players) so there is no emptiness re-check/early-return.
            playerIds = TsArray.Remove(playerIds, new string[] { null });
            playerIds = TsArray.Dedupe(playerIds);
            LastPlayerIds = playerIds;
            OnTrackingStarted(playerIds);
            TsEmit(OnTrackingStartedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the process stops.
        /// Read <c>LastPlayerIds</c> to check if the local player is tracked.
        /// </summary>
        // maxEventsPerSecond: 100. Same rationale as NotifyTrackedPlayersProcessStarted.
        // If this were at 5/s and NotifyTrackedPlayersAdded/Removed fired 6+ times in rapid
        // succession before a stop, this event would skip queued add/remove events and arrive
        // first, leaving LastPlayerIds incomplete when OnTrackingStopped fires.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersProcessStopped(string[] playerIds)
        {
            if (playerIds == null) return;
            // Owner-only guard: same rationale as NotifyTrackedPlayersProcessStarted.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Sanitize independently of the sender: same rationale as
            // NotifyTrackedPlayersProcessStarted above.
            playerIds = TsArray.Remove(playerIds, new string[] { null });
            playerIds = TsArray.Dedupe(playerIds);
            LastPlayerIds = playerIds;
            OnTrackingStopped(playerIds);
            TsEmit(OnTrackingStoppedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the process completes.
        /// Read <c>LastPlayerIds</c> to check if the local player is tracked.
        /// </summary>
        // maxEventsPerSecond: 100. Same rationale as NotifyTrackedPlayersProcessStarted.
        // Same ordering concern as NotifyTrackedPlayersProcessStopped.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersProcessCompleted(string[] playerIds)
        {
            if (playerIds == null) return;
            // Owner-only guard: same rationale as NotifyTrackedPlayersProcessStarted.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Sanitize independently of the sender: same rationale as
            // NotifyTrackedPlayersProcessStarted above.
            playerIds = TsArray.Remove(playerIds, new string[] { null });
            playerIds = TsArray.Dedupe(playerIds);
            LastPlayerIds = playerIds;
            OnTrackingCompleted(playerIds);
            TsEmit(OnTrackingCompletedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when players are added.
        /// Read <c>LastAddedPlayerIds</c> in your callback.
        /// </summary>
        // maxEventsPerSecond: 100. Same rationale as NotifyTrackedPlayersProcessStarted.
        // Without this, rapid add sequences (6+ per second) would queue this event while
        // NotifyTrackedPlayersProcessStopped/Completed (100/s) skip ahead, leaving
        // LastPlayerIds incomplete when lifecycle callbacks fire on remote clients.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersAdded(string[] addedPlayerIds)
        {
            // A non-null but empty array represents no actual change; every sender already
            // guards against this before broadcasting (RequestAddTrackedPlayers returns early
            // when its filtered validCount is 0), but this method is public and network-callable,
            // so a direct call could otherwise still fire a spurious "players added" notification
            // for zero players.
            if (addedPlayerIds == null || addedPlayerIds.Length == 0) return;
            // Owner-only guard: same rationale as NotifyTrackedPlayersProcessStarted. Checked
            // before sanitizing below so a rejected call doesn't pay for the dedup/null-strip
            // work at all.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Sanitize independently of the sender: RequestAddTrackedPlayers already dedupes
            // and null-filters before broadcasting, but this method is itself public and
            // [NetworkCallable], so a direct call from the real owner (passing the guard above)
            // could still bypass that. Strip nulls first, then dedupe (order matters: dedupe
            // alone would keep one null; null-strip alone wouldn't catch duplicate real ids).
            // Re-check emptiness afterward - stripping nulls out of e.g. [null] leaves nothing,
            // and per the guard above an empty payload must never fire the lifecycle hook/event.
            addedPlayerIds = TsArray.Remove(addedPlayerIds, new string[] { null });
            addedPlayerIds = TsArray.Dedupe(addedPlayerIds);
            if (addedPlayerIds.Length == 0) return;
            LastAddedPlayerIds = addedPlayerIds;
            // Apply the delta so LastPlayerIds is current when the callback fires.
            // Serialization packets and network events have no relative ordering guarantee
            // (VRChat docs), so LastPlayerIds may already include some of these entries;
            // deduplicate to match the invariant enforced by RequestAddTrackedPlayers.
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
        // maxEventsPerSecond: 100. Same rationale as NotifyTrackedPlayersAdded.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersRemoved(string[] removedPlayerIds)
        {
            // Any [NetworkCallable] can receive null parameters; TsArray.Remove crashes on null input.
            // A non-null but empty array represents no actual change; see NotifyTrackedPlayersAdded's
            // comment for why this is guarded here too, not just at the sender.
            if (removedPlayerIds == null || removedPlayerIds.Length == 0) return;
            // Owner-only guard: same rationale as NotifyTrackedPlayersProcessStarted. Checked
            // before sanitizing below so a rejected call doesn't pay for the dedup/null-strip
            // work at all.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Sanitize independently of the sender: same rationale as NotifyTrackedPlayersAdded.
            // TsArray.Remove already tolerates duplicate/null entries in removedPlayerIds without
            // corrupting LastPlayerIds itself, but without this LastRemovedPlayerIds - a direct,
            // unfiltered assignment below - would still misreport a null or a duplicated id as
            // having been removed.
            removedPlayerIds = TsArray.Remove(removedPlayerIds, new string[] { null });
            removedPlayerIds = TsArray.Dedupe(removedPlayerIds);
            if (removedPlayerIds.Length == 0) return;
            LastRemovedPlayerIds = removedPlayerIds;
            // Apply the delta so LastPlayerIds is current when the callback runs.
            LastPlayerIds = TsArray.Remove(LastPlayerIds, removedPlayerIds);
            OnTrackingPlayersRemoved(removedPlayerIds);
            TsEmit(OnTrackingPlayersRemovedEvent);
        }

        [NetworkCallable]
        public void RequestAddTrackedPlayers(int[] playerIds)
        {
            if (!CanHandleTrackedPlayersRequest(nameof(RequestAddTrackedPlayers), playerIds)) return;

            int[] newPlayerIds = TsArray.Dedupe(TsArray.Remove(playerIds, _trackedPlayerIds));
            if (newPlayerIds.Length == 0)
            {
                LogInfo("RequestAddTrackedPlayers ignored: all of the given player IDs are already tracked.");
                return;
            }

            _trackedPlayerIds = TsArray.Add(_trackedPlayerIds, newPlayerIds);
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersAdded), newPlayerIds);
        }

        [NetworkCallable]
        public void RequestRemoveTrackedPlayers(int[] playerIds)
        {
            if (!CanHandleTrackedPlayersRequest(nameof(RequestRemoveTrackedPlayers), playerIds)) return;

            int[] removedPlayerIds = TsArray.Dedupe(TsArray.Intersect(playerIds, _trackedPlayerIds));
            if (removedPlayerIds.Length == 0)
            {
                LogInfo("RequestRemoveTrackedPlayers ignored: none of the given player IDs are tracked.");
                return;
            }

            _trackedPlayerIds = TsArray.Remove(_trackedPlayerIds, removedPlayerIds);
            RequestSerialization();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersRemoved), removedPlayerIds);
        }

        private bool CanHandleTrackedPlayersRequest(string methodName, int[] playerIds)
        {
            if (!IsProcessOwner())
            {
                LogWarning(methodName + " rejected: not the owner.");
                return false;
            }

            if (!IsProcessRunning())
            {
                LogWarning(methodName + " rejected: not running.");
                return false;
            }

            if (playerIds == null || playerIds.Length == 0)
            {
                LogWarning(methodName + " ignored: no player IDs given.");
                return false;
            }

            return true;
        }
    }
}
