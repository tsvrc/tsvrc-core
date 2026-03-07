using UdonSharp;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsDataTransferer : TsDataReceiver
    {
        private UdonSharpBehaviour _transfererListener;
        private string _onStartedEvent = "OnTransferStarted";
        private string _onStoppedEvent = "OnTransferStopped";
        private string _onCompletedEvent = "OnTransferCompleted";
        private string _onChunkEvent = "OnTransferChunk";

        /// <summary>
        /// Initializes the TsDataTransferer with a listener and event method names.
        /// On each transfer event, SendCustomEvent is called on the listener using the corresponding name.
        /// Use nameof() for event names to avoid magic strings and get refactor safety.
        /// Read event data from the Last* properties inside the listener's callback methods:
        /// <list type="bullet">
        /// <item><term>onStartedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onStoppedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onCompletedEvent</term><description>LastData, LastPlayerIds</description></item>
        /// <item><term>onChunkEvent</term><description>LastChunkIndex, LastTotalChunks</description></item>
        /// </list>
        /// <example>
        /// <code>
        /// transferer.TsConstruct(
        ///     this,
        ///     nameof(OnTransferStartedMethod),
        ///     nameof(OnTransferStoppedMethod),
        ///     nameof(OnTransferCompletedMethod),
        ///     nameof(OnTransferChunkMethod)
        /// );
        ///
        /// public void OnTransferCompleted()
        /// {
        ///     var data = transferer.LastData;
        /// }
        /// </code>
        /// </example>
        /// </summary>
        public void TsConstructDataTransferer(
            UdonSharpBehaviour listener,
            string onStartedEvent,
            string onStoppedEvent,
            string onCompletedEvent,
            string onChunkEvent
        )
        {
            _transfererListener = listener;
            _onStartedEvent = onStartedEvent;
            _onStoppedEvent = onStoppedEvent;
            _onCompletedEvent = onCompletedEvent;
            _onChunkEvent = onChunkEvent;

            base.TsConstructDataReceiver(
                this,
                nameof(_OnDataReceptionStarted),
                nameof(_OnDataReceptionStopped),
                nameof(_OnDataReceptionCompleted),
                nameof(_OnDataChunkReceived)
            );
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
            _transfererListener.SendCustomEvent(_onStartedEvent);
        }

        public void _OnDataReceptionStopped()
        {
            _transfererListener.SendCustomEvent(_onStoppedEvent);
        }

        public void _OnDataReceptionCompleted()
        {
            _transfererListener.SendCustomEvent(_onCompletedEvent);
        }

        public void _OnDataChunkReceived()
        {
            _transfererListener.SendCustomEvent(_onChunkEvent);
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
