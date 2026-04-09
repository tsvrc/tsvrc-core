using Tsvrc.Player;
using Tsvrc.Utils;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    public class TsDataReceiver : TsDataSender
    {
        public const string OnDataReceptionStartedEvent = "OnDataReceptionStarted";
        public const string OnDataReceptionStoppedEvent = "OnDataReceptionStopped";
        public const string OnDataReceptionCompletedEvent = "OnDataReceptionCompleted";
        public const string OnDataChunkReceivedEvent = "OnDataChunkReceived";

        protected string[] _receivedChunks = new string[0];

        public string LastData { get; private set; } = "";
        public int LastChunkIndex { get; private set; } = 0;
        public int LastTotalChunks { get; private set; } = 0;

        /// <summary>
        /// Initializes the TsDataReceiver. Use <see cref="TsvrcBehaviour.TsSubscribe"/> to register
        /// listeners for the events defined as constants on this class.
        /// Read event data from the <c>Last*</c> properties inside your callback methods:
        /// <list type="bullet">
        /// <item><term><see cref="OnDataReceptionStartedEvent"/></term><description>no data</description></item>
        /// <item><term><see cref="OnDataReceptionStoppedEvent"/></term><description>no data</description></item>
        /// <item><term><see cref="OnDataReceptionCompletedEvent"/></term><description><c>LastData</c></description></item>
        /// <item><term><see cref="OnDataChunkReceivedEvent"/></term><description><c>LastChunkIndex</c>, <c>LastTotalChunks</c></description></item>
        /// </list>
        /// </summary>
        protected void TsConstructDataReceiver()
        {
            TsConstructDataSender();
            TsSubscribe(this, OnDataTransferStartedEvent, nameof(_OnDataTransferStarted));
            TsSubscribe(this, OnDataTransferStoppedEvent, nameof(_OnDataTransferStopped));
            TsSubscribe(this, OnDataTransferCompletedEvent, nameof(_OnDataTransferCompleted));
        }

        #region TsDataSender Callbacks

        public void _OnDataTransferStarted()
        {
            _receivedChunks = new string[0];

            TsEmit(OnDataReceptionStartedEvent);
        }

        public void _OnDataTransferStopped()
        {
            _receivedChunks = new string[0];

            TsEmit(OnDataReceptionStoppedEvent);
        }

        public void _OnDataTransferCompleted()
        {
            LastData = ReassembleMessage(_receivedChunks);
            _receivedChunks = new string[0];

            TsEmit(OnDataReceptionCompletedEvent);
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

            TsEmit(OnDataChunkReceivedEvent);
        }

        #endregion
    }
}
