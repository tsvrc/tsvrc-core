using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.TsNetworking.Utils
{
    public class TsvrcDataReceiver : TsvrcDataSender
    {
        protected string[] _receivedChunks = new string[0];

        #region Tsvrc Callbacks

        protected override void OnDataTransferStarted()
        {
            base.OnDataTransferStarted();

            _receivedChunks = new string[0];
            OnDataReceptionStarted();
        }

        protected override void OnDataTransferCompleted()
        {
            base.OnDataTransferCompleted();

            string completeMessage = ReassembleMessage(_receivedChunks);
            _receivedChunks = new string[0];
            OnDataReceptionCompleted(completeMessage);
        }

        protected override void OnDataTransferCancelled()
        {
            base.OnDataTransferCancelled();

            _receivedChunks = new string[0];

            OnDataReceptionCancelled();
        }

        protected override void OnSendDataChunkRequested(string dataChunk, int chunkIndex, int totalChunks)
        {
            base.OnSendDataChunkRequested(dataChunk, chunkIndex, totalChunks);

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastDataChunkReceived), dataChunk, chunkIndex, totalChunks);
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when data reception starts.
        /// This method is intended to be called on the tracker players and the process owner.
        /// Make sure to invoke base.OnDataReceptionStarted if overridden.
        /// </summary>
        protected virtual void OnDataReceptionStarted() { }

        /// <summary>
        /// Called when a data chunk is received.
        /// This method is intended to be called on the tracker players and the process owner.
        /// Make sure to invoke base.OnDataChunkReceived if overridden.
        /// </summary>
        /// <param name="chunkIndex">The index of the received chunk (1-based).</param>
        protected virtual void OnDataChunkReceived(int chunkIndex, int totalChunks) { }

        /// <summary>
        /// Called when the complete message is received.
        /// This method is intended to be called on the tracker players and the process owner.
        /// Make sure to invoke base.OnDataTransferCompleted if overridden.
        /// </summary>
        protected virtual void OnDataReceptionCompleted(string data) { }

        /// <summary>
        /// Called when the data reception is cancelled.
        /// This method is intended to be called on the tracker players and the process owner.
        /// Make sure to invoke base.OnDataReceptionCancelled if overridden.
        /// </summary>
        protected virtual void OnDataReceptionCancelled() { }

        #endregion

        #region Network Events

        /// <summary>
        /// Network event to broadcast the reception of a data chunk.
        /// </summary>
        [NetworkCallable]
        public void BroadcastDataChunkReceived(string dataChunk, int chunkIndex, int totalChunks)
        {
            var playerId = TsPlayerUtils.GetPlayerID(Networking.LocalPlayer);
            if (!IsTrackedPlayer(playerId) && !IsProcessOwner()) return;

            if (_receivedChunks.Length != totalChunks)
            {
                _receivedChunks = new string[totalChunks];
            }

            _receivedChunks[chunkIndex - 1] = dataChunk;

            NotifyChunkReceived();
            OnDataChunkReceived(chunkIndex, totalChunks);
        }

        #endregion

        #region Private Methods

        private void NotifyChunkReceived()
        {
            SetReady();
        }

        private string ReassembleMessage(string[] receivedChunks)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < receivedChunks.Length; i++)
            {
                sb.Append(receivedChunks[i]);
            }
            return sb.ToString();
        }

        #endregion
    }
}
