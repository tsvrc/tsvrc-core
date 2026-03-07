using Tsvrc.Player;
using Tsvrc.Utils;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.TsNetworking.Utils
{
    public class TsvrcDataReceiver : TsvrcDataSender
    {
        protected string[] _receivedChunks = new string[0];

        #region TsvrcDataSender Callbacks

        protected override void OnDataTransferStartedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataTransferStartedAsTrackedPlayer(playerIds);

            _receivedChunks = new string[0];
            OnDataReceptionStartedAsTrackedPlayer(playerIds);
        }

        protected override void OnDataTransferStoppedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataTransferStoppedAsTrackedPlayer(playerIds);

            _receivedChunks = new string[0];
            OnDataReceptionStoppedAsTrackedPlayer(playerIds);
        }

        protected override void OnDataTransferCompletedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataTransferCompletedAsTrackedPlayer(playerIds);

            string completeMessage = ReassembleMessage(_receivedChunks);
            _receivedChunks = new string[0];
            OnDataReceptionCompletedAsTrackedPlayer(completeMessage, playerIds);
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

        #region Virtual Methods

        /// <summary>
        /// Called when data reception starts on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// This fires once at the beginning of the reception (first chunk).
        /// For owner-only logic, override OnProcessStarted() from the base class.
        /// </summary>
        protected virtual void OnDataReceptionStartedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when data reception is stopped on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// For owner-only logic, override OnProcessStopped() from the base class.
        /// </summary>
        protected virtual void OnDataReceptionStoppedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when data reception completes on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// This fires once at the end when all chunks are received.
        /// For owner-only logic, override OnProcessCompleted() from the base class.
        /// </summary>
        /// <param name="data">The complete reassembled data.</param>
        /// <param name="playerIds">The player IDs involved in the reception.</param>
        protected virtual void OnDataReceptionCompletedAsTrackedPlayer(string data, string[] playerIds) { }

        /// <summary>
        /// Called when a data chunk is received on tracked players.
        /// This fires for each chunk received.
        /// </summary>
        /// <param name="chunkIndex">The index of the received chunk (1-based).</param>
        /// <param name="totalChunks">The total number of chunks to receive.</param>
        protected virtual void OnDataChunkReceivedAsTrackedPlayer(int chunkIndex, int totalChunks) { }

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

            NotifyChunkReceived();
            OnDataChunkReceivedAsTrackedPlayer(chunkIndex, totalChunks);
        }

        #endregion
    }
}
