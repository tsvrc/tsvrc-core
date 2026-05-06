using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    /// <summary>
    /// Sends data transfer lifecycle notifications (started, stopped, completed) to all players
    /// in the instance using <see cref="VRC.SDK3.UdonNetworkCalling.NetworkCallable"/> methods.
    /// Hooks into the <see cref="ChunkedTransferSession"/> sequence callbacks and translates them
    /// into network events. Stop and completion notifications are deferred by one frame so that
    /// internal cleanup finishes before user callbacks run.
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

        // These flags track pending deferred emissions. TsEmit fires synchronously while
        // ExecuteStop or ExecuteComplete is still on the call stack before InternalCleanup runs.
        // Deferring to the next frame ensures state is fully cleaned up before user callbacks fire.
        // Not reset by ResetInternalTransferData because they must survive until _EmitDataTransfer*
        // fires. NotifyTrackedPlayersDataTransferStarted clears both to suppress stale emissions
        // when a new transfer begins before the deferred call runs.
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
        // maxEventsPerSecond must match BroadcastDataChunkReceived (both 100 per second).
        // VRChat only guarantees event ordering when you stay under your own defined rate limit.
        // At the default 5 per second, rapid CancelDataTransfer and TransferData calls can cause
        // chunk events to arrive on remote clients before this started notification. When that
        // happens, _transferActive is false when the first chunk arrives, the chunk is dropped,
        // SetReady is never called, and the transfer stalls indefinitely.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferStarted()
        {
            // Only the object owner should trigger this event. Any player can call a
            // NetworkCallable directly, so without this check a malicious player could reset
            // receiver state on all clients, corrupting _expectedSenderId and causing the real
            // owner's chunks to be rejected, stalling the transfer permanently.
            // _isBroadcasting bypasses this check during the owner's own local execution
            // where CallingPlayer may not be propagated correctly.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Clear any pending deferred emissions from a previous transfer so they do not
            // fire after this new transfer has already started.
            _pendingTransferStopped = false;
            _pendingTransferCompleted = false;
            OnTransferStarted();
            TsEmit(OnDataTransferStartedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer is stopped.
        /// </summary>
        // maxEventsPerSecond must match BroadcastDataChunkReceived at 100 per second.
        // Same ordering rationale as NotifyTrackedPlayersDataTransferStarted.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferStopped()
        {
            // Same owner check as NotifyTrackedPlayersDataTransferStarted.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Set the flag before calling the virtual method. If OnTransferStopped triggers
            // a new TransferData call, NotifyTrackedPlayersDataTransferStarted will run inline
            // and clear this flag. Setting it afterward would override that clear and emit a
            // spurious stopped notification.
            _pendingTransferStopped = true;
            OnTransferStopped();
            // Defer the emit so InternalCleanup finishes first. On the owner, ExecuteStop is
            // still on the call stack here, so calling TransferData from the callback would
            // corrupt internal state without this deferral.
            SendCustomEventDelayedSeconds(nameof(_EmitDataTransferStopped), 0f);
        }

        // Deferred emit scheduled by NotifyTrackedPlayersDataTransferStopped.
        // The underscore prefix prevents this from being callable as a network event.
        public void _EmitDataTransferStopped()
        {
            // If a new transfer started before this ran, the flag was already cleared, so skip.
            if (!_pendingTransferStopped) return;
            _pendingTransferStopped = false;
            TsEmit(OnDataTransferStoppedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer completes.
        /// </summary>
        // maxEventsPerSecond must match BroadcastDataChunkReceived at 100 per second.
        // Same ordering rationale as NotifyTrackedPlayersDataTransferStarted.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferCompleted()
        {
            // Same owner check as NotifyTrackedPlayersDataTransferStarted.
            if (!_isBroadcasting)
            {
                var caller = NetworkCalling.CallingPlayer;
                var owner = Networking.GetOwner(gameObject);
                if (caller == null || owner == null || caller.playerId != owner.playerId) return;
            }
            // Set the flag before calling the virtual method, same reason as in
            // NotifyTrackedPlayersDataTransferStopped.
            _pendingTransferCompleted = true;
            OnTransferCompleted();
            // Defer the emit so InternalCleanup finishes before user callbacks run.
            SendCustomEventDelayedSeconds(nameof(_EmitDataTransferCompleted), 0f);
        }

        // Deferred emit scheduled by NotifyTrackedPlayersDataTransferCompleted.
        // The underscore prefix prevents this from being callable as a network event.
        public void _EmitDataTransferCompleted()
        {
            // If a new transfer started before this ran, the flag was already cleared, so skip.
            if (!_pendingTransferCompleted) return;
            _pendingTransferCompleted = false;
            TsEmit(OnDataTransferCompletedEvent);
        }
    }
}
