using UdonSharp;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DataTransferer : DataSenderReceiver
    {
        /// <summary>
        /// Emitted when the transfer starts.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTransferStartedEvent = "OnTransferStarted";
        /// <summary>
        /// Emitted when the transfer is stopped before completion.
        /// Read <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTransferStoppedEvent = "OnTransferStopped";
        /// <summary>
        /// Emitted when the transfer completes.
        /// Read <c>LastData</c> and <c>LastPlayerIds</c> in your callback.
        /// </summary>
        public const string OnTransferCompletedEvent = "OnTransferCompleted";
        /// <summary>
        /// Emitted when a chunk is transferred.
        /// Read <c>LastChunkIndex</c> and <c>LastTotalChunks</c> in your callback.
        /// </summary>
        public const string OnTransferChunkEvent = "OnTransferChunk";

        protected override void TsStart()
        {
            base.TsStart();
            TsSubscribe(this, OnDataReceptionStartedEvent, nameof(_OnDataReceptionStarted));
            TsSubscribe(this, OnDataReceptionStoppedEvent, nameof(_OnDataReceptionStopped));
            TsSubscribe(this, OnDataReceptionCompletedEvent, nameof(_OnDataReceptionCompleted));
            TsSubscribe(this, OnDataChunkReceivedEvent, nameof(_OnDataChunkReceived));
        }

        #region TsvrcProcess Callbacks

        protected override void OnOwnerAbandonedProcess()
        {
            base.OnOwnerAbandonedProcess();

            CancelDataTransfer();
        }

        #endregion

        #region DataReceiver Callbacks

        public void _OnDataReceptionStarted()
        {
            TsEmit(OnTransferStartedEvent);
        }

        public void _OnDataReceptionStopped()
        {
            TsEmit(OnTransferStoppedEvent);
        }

        public void _OnDataReceptionCompleted()
        {
            TsEmit(OnTransferCompletedEvent);
        }

        public void _OnDataChunkReceived()
        {
            TsEmit(OnTransferChunkEvent);
        }

        #endregion

        #region Public Methods

        public override void TransferData(string data, string[] playerIds)
        {
            base.TransferData(data, playerIds);
        }

        public override void CancelDataTransfer()
        {
            base.CancelDataTransfer();
        }

        #endregion
    }
}
