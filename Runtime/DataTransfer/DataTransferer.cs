using UdonSharp;

namespace Tsvrc.DataTransfer
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DataTransferer : DataSenderReceiver
    {
        /// <summary>
        /// Emitted when the transfer starts.
        /// <c>LastPlayerIds</c> is populated (carried over from the preceding <c>OnTrackingStarted</c> broadcast).
        /// </summary>
        public const string OnTransferStartedEvent = "OnTransferStarted";
        /// <summary>
        /// Emitted when the transfer is stopped before completion.
        /// <c>LastPlayerIds</c> is populated (carried over from the preceding <c>OnTrackingStopped</c> broadcast).
        /// </summary>
        public const string OnTransferStoppedEvent = "OnTransferStopped";
        /// <summary>
        /// Emitted when the transfer completes.
        /// Read <c>LastData</c> in your callback.
        /// <c>LastPlayerIds</c> is also populated (carried over from the preceding <c>OnTrackingCompleted</c> broadcast).
        /// </summary>
        public const string OnTransferCompletedEvent = "OnTransferCompleted";
        /// <summary>
        /// Emitted when a chunk is transferred.
        /// Read <c>LastChunkIndex</c> and <c>LastTotalChunks</c> in your callback.
        /// </summary>
        public const string OnTransferChunkEvent = "OnTransferChunk";

        #region TsvrcProcess Callbacks

        protected override void OnOwnerAbandonedProcess()
        {
            // DataSender.OnOwnerAbandonedProcess (called via base) already calls StopReadyCheck(),
            // which executes ExecuteStop() → broadcasts NotifyTrackedPlayersDataTransferStopped
            // and clears all process/transfer state via InternalCleanup. A second CancelDataTransfer()
            // here would call StopReadyCheck() with _isRunning=false, producing a spurious
            // "[TsvrcProcess] Process is not running" warning on every ownership transfer.
            base.OnOwnerAbandonedProcess();
        }

        #endregion

        #region DataSenderReceiver Overrides

        protected override void OnDataReceptionStarted() => TsEmit(OnTransferStartedEvent);
        protected override void OnDataReceptionStopped() => TsEmit(OnTransferStoppedEvent);
        protected override void OnDataReceptionCompleted() => TsEmit(OnTransferCompletedEvent);
        protected override void OnDataChunkReceived() => TsEmit(OnTransferChunkEvent);

        #endregion
    }
}
