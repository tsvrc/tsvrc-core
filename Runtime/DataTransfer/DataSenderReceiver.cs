namespace Tsvrc.DataTransfer
{
    /// <summary>
    /// Adds receiver-side lifecycle events, <c>Last*</c> public properties, and deferred emission
    /// on top of <see cref="DataChunkReceiver"/>.
    /// Overrides the <see cref="DataChunkReceiver"/> hooks to populate properties and emit events,
    /// then exposes virtual methods for subclasses to react without touching raw state.
    /// </summary>
    public class DataSenderReceiver : DataChunkReceiver
    {
        /// <summary>
        /// Emitted when data reception starts.
        /// No <c>Last*</c> properties are updated.
        /// </summary>
        public const string OnDataReceptionStartedEvent = "OnDataReceptionStarted";
        /// <summary>
        /// Emitted when data reception is stopped before completion.
        /// No <c>Last*</c> properties are updated.
        /// </summary>
        public const string OnDataReceptionStoppedEvent = "OnDataReceptionStopped";
        /// <summary>
        /// Emitted when data reception completes.
        /// Read <c>LastData</c> in your callback.
        /// </summary>
        public const string OnDataReceptionCompletedEvent = "OnDataReceptionCompleted";
        /// <summary>
        /// Emitted when a chunk is received.
        /// Read <c>LastChunkIndex</c> and <c>LastTotalChunks</c> in your callback.
        /// </summary>
        public const string OnDataChunkReceivedEvent = "OnDataChunkReceived";

        // Staging fields for the deferred completion emit (see OnChunksAssembled).
        // _pendingCompletionData holds the assembled string until the deferred emit fires, so
        // a new TransferData() call (which resets LastData = "" via ResetReceiverState())
        // cannot corrupt the pending data. _pendingCompletion guards against a spurious emit
        // if a new transfer starts before the deferred call fires: ResetReceiverState() clears
        // it to false, causing _EmitDataReceptionCompleted to return early.
        private bool _pendingCompletion = false;
        private string _pendingCompletionData = "";

        // Mirror of _pendingCompletion for the stopped path. OnTransferStopped defers the emit
        // so InternalCleanup finishes before user callbacks run. A TransferData() call from the
        // callback would otherwise fire inside ExecuteStop and corrupt the new transfer's state.
        // ResetReceiverState() clears this to suppress a stale emit if a new transfer starts
        // before the deferred call fires.
        private bool _pendingStop = false;

        public string LastData { get; private set; } = "";
        public int LastChunkIndex { get; private set; } = 0;
        public int LastTotalChunks { get; private set; } = 0;

        protected override void OnTransferStarted()
        {
            base.OnTransferStarted(); // DataChunkReceiver: ResetReceiverState + _transferActive + sender ID
            OnDataReceptionStarted();
            TsEmit(OnDataReceptionStartedEvent);
        }

        protected override void OnTransferStopped()
        {
            base.OnTransferStopped(); // DataChunkReceiver: ResetReceiverState (also clears _pendingStop)
            // Set AFTER base so ResetReceiverState's clear does not race with this assignment.
            _pendingStop = true;
            // Defer so InternalCleanup finishes before user callbacks run (same reason as OnChunksAssembled).
            SendCustomEventDelayedSeconds(nameof(_EmitDataReceptionStopped), 0f);
        }

        protected override void OnChunksAssembled(string assembledData)
        {
            // Stage assembled data: LastData is assigned in _EmitDataReceptionCompleted atomically
            // with the emission. If a new transfer starts before the deferred emit fires,
            // ResetReceiverState() clears _pendingCompletion, causing the early return before
            // _pendingCompletionData is read (ResetReceiverState also clears that field).
            _pendingCompletionData = assembledData;
            _pendingCompletion = true;

            // Defer so InternalCleanup finishes before user callbacks run. Without deferral,
            // TransferData() from the completion callback fires inside ExecuteComplete before
            // InternalCleanup, corrupting the new transfer's process state.
            SendCustomEventDelayedSeconds(nameof(_EmitDataReceptionCompleted), 0f);
        }

        protected override void OnChunkStored(int chunkIndex, int totalChunks)
        {
            LastChunkIndex = chunkIndex;
            LastTotalChunks = totalChunks;

            // Emit before NotifyChunkReceived() (called by DataChunkReceiver after OnChunkStored):
            // on the owner, NotifyChunkReceived() can synchronously trigger completion.
            // Emitting first guarantees chunk events always precede completion events.
            OnDataChunkReceived();
            TsEmit(OnDataChunkReceivedEvent);
        }

        protected override void ResetReceiverState()
        {
            base.ResetReceiverState(); // DataChunkReceiver: clears transfer-active + chunk fields
            LastData = "";
            LastChunkIndex = 0;
            LastTotalChunks = 0;
            // Clear staging fields so the deferred _EmitDataReceptionCompleted/_EmitDataReceptionStopped
            // from a previous transfer see their flags as false and return early, preventing spurious
            // events with stale data after a new transfer start.
            _pendingCompletion = false;
            _pendingCompletionData = "";
            _pendingStop = false;
        }

        // Underscore prefix: local-only, cannot be triggered via network event.
        // Called by SendCustomEventDelayedSeconds in OnTransferStopped.
        public void _EmitDataReceptionStopped()
        {
            // Guard: ResetReceiverState (called by OnTransferStarted or a new
            // TransferData start) clears _pendingStop = false. Without this, a new transfer
            // starting before this fires would deliver a spurious OnDataReceptionStoppedEvent.
            if (!_pendingStop) return;
            _pendingStop = false;
            OnDataReceptionStopped();
            TsEmit(OnDataReceptionStoppedEvent);
        }

        // Underscore prefix: local-only, cannot be triggered via network event.
        // Called by SendCustomEventDelayedSeconds in OnChunksAssembled.
        public void _EmitDataReceptionCompleted()
        {
            // Guard: ResetReceiverState (called from OnTransferStarted on a new TransferData start)
            // clears _pendingCompletion = false. Without this check, a new transfer starting between
            // the deferral and this firing would deliver a spurious OnDataReceptionCompletedEvent
            // with empty LastData to the new subscribers.
            if (!_pendingCompletion) return;
            _pendingCompletion = false;

            // Reachable only when _pendingCompletion was not cleared by ResetReceiverState,
            // so _pendingCompletionData is guaranteed to contain valid assembled data.
            LastData = _pendingCompletionData;
            _pendingCompletionData = "";

            OnDataReceptionCompleted();
            TsEmit(OnDataReceptionCompletedEvent);
        }

        /// <summary>Called on all clients when data reception starts.</summary>
        protected virtual void OnDataReceptionStarted() { }
        /// <summary>Called on all clients when data reception is stopped before completion.</summary>
        protected virtual void OnDataReceptionStopped() { }
        /// <summary>Called on all clients when data reception completes. <c>LastData</c> is set before this fires.</summary>
        protected virtual void OnDataReceptionCompleted() { }
        /// <summary>Called on all tracked clients when a data chunk is received. <c>LastChunkIndex</c> and <c>LastTotalChunks</c> are set before this fires.</summary>
        protected virtual void OnDataChunkReceived() { }
    }
}
