using Tsvrc.Player;
using Tsvrc.Utils;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    public class DataReceiver : DataSender
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

        protected string[] _receivedChunks = new string[0];

        public string LastData { get; private set; } = "";
        public int LastChunkIndex { get; private set; } = 0;
        public int LastTotalChunks { get; private set; } = 0;

        protected override void TsStart()
        {
            base.TsStart();
            TsSubscribe(this, OnDataTransferStartedEvent, nameof(_OnDataTransferStarted));
            TsSubscribe(this, OnDataTransferStoppedEvent, nameof(_OnDataTransferStopped));
            TsSubscribe(this, OnDataTransferCompletedEvent, nameof(_OnDataTransferCompleted));
        }

        #region TsvrcProcess Callbacks

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            _receivedChunks = new string[0];
            LastData = "";
            LastChunkIndex = 0;
            LastTotalChunks = 0;
        }

        #endregion

        #region DataSender Callbacks

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
