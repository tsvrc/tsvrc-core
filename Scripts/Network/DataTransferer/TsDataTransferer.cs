using UdonSharp;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsDataTransferer : TsDataReceiver
    {
        private UdonSharpBehaviour _listener;
        private string _onStartedEvent = "OnTransferStarted";
        private string _onStoppedEvent = "OnTransferStopped";
        private string _onCompletedEvent = "OnTransferCompleted";
        private string _onChunkEvent = "OnTransferChunk";

        public string LastData { get; private set; } = "";
        public string[] LastPlayerIds { get; private set; } = new string[0];
        public int LastChunkIndex { get; private set; } = 0;
        public int LastTotalChunks { get; private set; } = 0;

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
        public void TsConstruct(
            UdonSharpBehaviour listener,
            string onStartedEvent,
            string onStoppedEvent,
            string onCompletedEvent,
            string onChunkEvent
        )
        {
            _listener = listener;
            _onStartedEvent = onStartedEvent;
            _onStoppedEvent = onStoppedEvent;
            _onCompletedEvent = onCompletedEvent;
            _onChunkEvent = onChunkEvent;
        }

        #region TsvrcProcess Callbacks

        protected override void OnOwnerAbandonedProcess()
        {
            base.OnOwnerAbandonedProcess();

            CancelDataTransfer();
        }

        #endregion

        #region TsDataReceiver Callbacks

        protected override void OnDataReceptionStartedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataReceptionStartedAsTrackedPlayer(playerIds);

            LastPlayerIds = playerIds;
            _listener.SendCustomEvent(_onStartedEvent);
        }

        protected override void OnDataReceptionStoppedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataReceptionStoppedAsTrackedPlayer(playerIds);

            LastPlayerIds = playerIds;
            _listener.SendCustomEvent(_onStoppedEvent);
        }

        protected override void OnDataReceptionCompletedAsTrackedPlayer(string data, string[] playerIds)
        {
            base.OnDataReceptionCompletedAsTrackedPlayer(data, playerIds);

            LastData = data;
            LastPlayerIds = playerIds;
            _listener.SendCustomEvent(_onCompletedEvent);
        }

        protected override void OnDataChunkReceivedAsTrackedPlayer(int chunkIndex, int totalChunks)
        {
            base.OnDataChunkReceivedAsTrackedPlayer(chunkIndex, totalChunks);

            LastChunkIndex = chunkIndex;
            LastTotalChunks = totalChunks;
            _listener.SendCustomEvent(_onChunkEvent);
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
