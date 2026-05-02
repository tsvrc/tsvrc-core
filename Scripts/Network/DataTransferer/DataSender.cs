using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    /// <summary>
    /// Broadcasts transfer lifecycle events (<c>Started</c>, <c>Stopped</c>, <c>Completed</c>)
    /// to all instance players via <see cref="VRC.SDK3.UdonNetworkCalling.NetworkCallable"/> methods.
    /// Receives the <see cref="ChunkedTransferSession"/> sequence hooks and translates them into
    /// network events. Defers <see cref="TsEmit"/> calls so <c>InternalCleanup</c> finishes
    /// before user callbacks fire.
    /// </summary>
    public class DataSender : ChunkedTransferSession
    {
        /// <summary>
        /// Emitted when the data transfer starts.
        /// No <c>Last*</c> properties are updated.
        /// </summary>
        public const string OnDataTransferStartedEvent = "OnDataTransferStarted";
        /// <summary>
        /// Emitted when the data transfer is stopped before completion.
        /// No <c>Last*</c> properties are updated.
        /// </summary>
        public const string OnDataTransferStoppedEvent = "OnDataTransferStopped";
        /// <summary>
        /// Emitted when the data transfer completes.
        /// No <c>Last*</c> properties are updated.
        /// </summary>
        public const string OnDataTransferCompletedEvent = "OnDataTransferCompleted";

        // Deferred-emission flags. TsEmit fires synchronously while ExecuteStop/ExecuteComplete
        // is still on the call stack (InternalCleanup not yet run). Deferring to the next event
        // cycle ensures InternalCleanup completes before user callbacks fire.
        // Not reset by ResetInternalTransferData(). They must survive until _EmitDataTransfer*
        // fires. Cleared only by NotifyTrackedPlayersDataTransferStarted to suppress stale emits.
        private bool _pendingTransferStopped = false;
        private bool _pendingTransferCompleted = false;

        protected override void OnChunkSequenceStarted()
        {
            _isBroadcasting = true;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStarted));
            _isBroadcasting = false;
        }

        protected override void OnChunkSequenceStopped()
        {
            _isBroadcasting = true;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStopped));
            _isBroadcasting = false;
        }

        protected override void OnChunkSequenceCompleted()
        {
            _isBroadcasting = true;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferCompleted));
            _isBroadcasting = false;
        }

        /// <summary>Called on all clients when the data transfer starts.</summary>
        protected virtual void OnTransferStarted() { }
        /// <summary>Called on all clients when the data transfer is stopped before completion.</summary>
        protected virtual void OnTransferStopped() { }
        /// <summary>Called on all clients when the data transfer completes successfully.</summary>
        protected virtual void OnTransferCompleted() { }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer starts.
        /// </summary>
        // maxEventsPerSecond: 100. Must match BroadcastDataChunkReceived (also 100/s) so that
        // per VRChat docs (creators.vrchat.com/worlds/udon/networking/events#rate-limiting):
        // "The order in which events are sent and received is guaranteed as long as you don't
        // hit your own defined rate-limit." At 5/s (default), rapid CancelDataTransfer+TransferData
        // cycles (>5/s) queue this event while the 100/s chunk event drains ahead of it. Remote
        // clients then receive BroadcastDataChunkReceived before NotifyTrackedPlayersDataTransferStarted,
        // so _transferActive is still false when the first chunk arrives, the chunk is dropped,
        // the recipient never calls SetReady(), and the transfer stalls indefinitely.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferStarted()
        {
            // Only the process owner sends this event. Any instance player can invoke a
            // [NetworkCallable] directly; without this guard a malicious player could call
            // OnTransferStarted() → ResetReceiverState() on all clients, corrupting
            // _expectedSenderId so the real owner's subsequent chunks are rejected, and
            // stalling the transfer permanently. _isBroadcasting bypasses the check during
            // the owner's own inline execution where CallingPlayer may be propagated.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Cancel any deferred stopped/completed emit from a previous transfer so it
            // does not fire after this new transfer has already started.
            _pendingTransferStopped = false;
            _pendingTransferCompleted = false;
            OnTransferStarted();
            TsEmit(OnDataTransferStartedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer is stopped.
        /// </summary>
        // maxEventsPerSecond: 100. Same rationale as NotifyTrackedPlayersDataTransferStarted:
        // must match BroadcastDataChunkReceived to preserve event ordering under load.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferStopped()
        {
            // Owner-only guard: same rationale as NotifyTrackedPlayersDataTransferStarted.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Set flag BEFORE the virtual callback. OnTransferStopped() fires synchronously, and
            // a subclass may call TransferData() inside it. That triggers
            // NotifyTrackedPlayersDataTransferStarted inline, clearing this flag. Setting after
            // the callback would re-set it after that clear, causing a spurious stopped event.
            _pendingTransferStopped = true;
            OnTransferStopped();
            // Defer TsEmit: on the owner this fires while ExecuteStop is still on the call stack
            // (InternalCleanup not yet run), so a TransferData() from the callback would corrupt state.
            SendCustomEventDelayedSeconds(nameof(_EmitDataTransferStopped), 0f);
        }

        // Underscore prefix: local-only, cannot be triggered via network event.
        // Called by SendCustomEventDelayedSeconds in NotifyTrackedPlayersDataTransferStopped.
        public void _EmitDataTransferStopped()
        {
            // Guard: NotifyTrackedPlayersDataTransferStarted clears _pendingTransferStopped,
            // suppressing this if a new transfer started before the deferred call fires.
            if (!_pendingTransferStopped) return;
            _pendingTransferStopped = false;
            TsEmit(OnDataTransferStoppedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer completes.
        /// </summary>
        // maxEventsPerSecond: 100. Same rationale as NotifyTrackedPlayersDataTransferStarted.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferCompleted()
        {
            // Owner-only guard: same rationale as NotifyTrackedPlayersDataTransferStarted.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Flag before virtual call: a new TransferData() from the callback would call
            // NotifyTrackedPlayersDataTransferStarted inline, clearing this flag; setting
            // after the call would re-set it, causing a spurious completed emit.
            _pendingTransferCompleted = true;
            OnTransferCompleted();
            // Defer so InternalCleanup finishes before user callbacks run
            // (same reason as NotifyTrackedPlayersDataTransferStopped).
            SendCustomEventDelayedSeconds(nameof(_EmitDataTransferCompleted), 0f);
        }

        // Underscore prefix: local-only, cannot be triggered via network event.
        // Called by SendCustomEventDelayedSeconds in NotifyTrackedPlayersDataTransferCompleted.
        public void _EmitDataTransferCompleted()
        {
            // Guard: NotifyTrackedPlayersDataTransferStarted clears _pendingTransferCompleted,
            // suppressing this if a new transfer started before the deferred call fires.
            if (!_pendingTransferCompleted) return;
            _pendingTransferCompleted = false;
            TsEmit(OnDataTransferCompletedEvent);
        }
    }
}
