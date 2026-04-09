using UdonSharp;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsDataTransferer : TsDataReceiver
    {
        public const string OnTransferStartedEvent = "OnTransferStarted";
        public const string OnTransferStoppedEvent = "OnTransferStopped";
        public const string OnTransferCompletedEvent = "OnTransferCompleted";
        public const string OnTransferChunkEvent = "OnTransferChunk";

        /// <summary>
        /// Initializes the TsDataTransferer. After calling this, use <see cref="TsvrcBehaviour.TsSubscribe"/>
        /// to register listeners for the transfer events defined as constants on this class.
        /// Read event data from the <c>Last*</c> properties inside your callback methods:
        /// <list type="bullet">
        /// <item><term><see cref="OnTransferStartedEvent"/></term><description><c>LastPlayerIds</c></description></item>
        /// <item><term><see cref="OnTransferStoppedEvent"/></term><description><c>LastPlayerIds</c></description></item>
        /// <item><term><see cref="OnTransferCompletedEvent"/></term><description><c>LastData</c>, <c>LastPlayerIds</c></description></item>
        /// <item><term><see cref="OnTransferChunkEvent"/></term><description><c>LastChunkIndex</c>, <c>LastTotalChunks</c></description></item>
        /// </list>
        /// <example>
        /// <code>
        /// transferer.TsConstructDataTransferer();
        /// transferer.TsSubscribe(this, TsDataTransferer.OnTransferCompletedEvent, nameof(_OnTransferCompleted));
        /// transferer.TsSubscribe(this, TsDataTransferer.OnTransferChunkEvent, nameof(_OnTransferChunk));
        ///
        /// public void _OnTransferCompleted()
        /// {
        ///     var data = transferer.LastData;
        /// }
        /// </code>
        /// </example>
        /// </summary>
        public void TsConstructDataTransferer()
        {
            TsConstructDataReceiver();
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

        #region TsDataReceiver Callbacks

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
