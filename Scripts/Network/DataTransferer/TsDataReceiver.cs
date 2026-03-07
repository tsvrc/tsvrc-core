using Tsvrc.Player;
using Tsvrc.Utils;
using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    public class TsDataReceiver : TsDataSender
    {
        protected string[] _receivedChunks = new string[0];

        public string LastData { get; private set; } = "";
        public int LastChunkIndex { get; private set; } = 0;
        public int LastTotalChunks { get; private set; } = 0;

        private UdonSharpBehaviour _dataReceiverListener;
        private string _onDataReceptionStartedEvent = "OnDataReceptionStarted";
        private string _onDataReceptionStoppedEvent = "OnDataReceptionStopped";
        private string _onDataReceptionCompletedEvent = "OnDataReceptionCompleted";
        private string _onDataChunkReceivedEvent = "OnDataChunkReceived";

        /// <summary>
        /// Initializes the TsDataReceiver with a listener and event method names.
        /// On each reception event, SendCustomEvent is called on the listener using the corresponding name.
        /// Use nameof() for event names to avoid magic strings and get refactor safety.
        /// Read event data from the Last* properties inside the listener's callback methods:
        /// <list type="bullet">
        /// <item><term>onDataReceptionStartedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onDataReceptionStoppedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onDataReceptionCompletedEvent</term><description>LastData, LastPlayerIds</description></item>
        /// <item><term>onDataChunkReceivedEvent</term><description>LastChunkIndex, LastTotalChunks</description></item>
        /// </list>
        /// <example>
        /// <code>
        /// receiver.TsConstruct(
        ///     this,
        ///     nameof(_OnDataReceptionStartedMethod),
        ///     nameof(_OnDataReceptionStoppedMethod),
        ///     nameof(_OnDataReceptionCompletedMethod),
        ///     nameof(_OnDataChunkReceivedMethod)
        /// );
        /// </code>
        /// </example>
        /// </summary>
        protected void TsConstructDataReceiver(
            UdonSharpBehaviour listener,
            string onDataReceptionStartedEvent,
            string onDataReceptionStoppedEvent,
            string onDataReceptionCompletedEvent,
            string onDataChunkReceivedEvent
        )
        {
            _dataReceiverListener = listener;
            _onDataReceptionStartedEvent = onDataReceptionStartedEvent;
            _onDataReceptionStoppedEvent = onDataReceptionStoppedEvent;
            _onDataReceptionCompletedEvent = onDataReceptionCompletedEvent;
            _onDataChunkReceivedEvent = onDataChunkReceivedEvent;

            base.TsConstructDataSender(
                this,
                nameof(_OnDataTransferStarted),
                nameof(_OnDataTransferStopped),
                nameof(_OnDataTransferCompleted)
            );
        }

        #region TsDataSender Callbacks

        public void _OnDataTransferStarted()
        {
            _receivedChunks = new string[0];

            _dataReceiverListener.SendCustomEvent(_onDataReceptionStartedEvent);
        }

        public void _OnDataTransferStopped()
        {
            _receivedChunks = new string[0];

            _dataReceiverListener.SendCustomEvent(_onDataReceptionStoppedEvent);
        }

        public void _OnDataTransferCompleted()
        {
            LastData = ReassembleMessage(_receivedChunks);
            _receivedChunks = new string[0];

            _dataReceiverListener.SendCustomEvent(_onDataReceptionCompletedEvent);
        }

        protected override void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            base.OnDataChunkSendRequested(dataChunk, chunkIndex, totalChunks, playerIds);

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastDataChunkReceived), dataChunk, chunkIndex, totalChunks, playerIds);
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Reassembles the complete message from received chunks.
        /// </summary>
        protected string ReassembleMessage(string[] receivedChunks)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < receivedChunks.Length; i++)
            {
                sb.Append(receivedChunks[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Notifies the sender that this chunk was received by marking ready.
        /// </summary>
        protected void NotifyChunkReceived()
        {
            SetReady();
        }

        #endregion

        #region Network Events

        /// <summary>
        /// Network callable method to broadcast the reception of a data chunk.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void BroadcastDataChunkReceived(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(playerIds, playerId)) return;

            if (_receivedChunks.Length != totalChunks)
            {
                _receivedChunks = new string[totalChunks];
            }

            _receivedChunks[chunkIndex - 1] = dataChunk;
            LastChunkIndex = chunkIndex;
            LastTotalChunks = totalChunks;

            NotifyChunkReceived();

            _dataReceiverListener.SendCustomEvent(_onDataChunkReceivedEvent);
        }

        #endregion
    }
}
