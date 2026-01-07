using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.TsNetworking.Utils
{
    public class TsvrcDataSender : TsvrcPlayerReadyChecker
    {
        protected const int CHUNK_SIZE = 15000;
        protected const int MAX_MESSAGE_SIZE = 500000;

        private string _initialData = "";
        private string[] _dataChunks = new string[0];
        // Current chunk index (1-based)
        private int _currentChunkIndex = 0;
        private int _totalChunks = 0;
        private string[] _targetPlayerIds = new string[0];

        #region Tsvrc Callbacks

        protected override void OnReadyCheckStarted(string[] playerIds)
        {
            base.OnReadyCheckStarted(playerIds);

            if (!IsProcessOwner()) return;

            if (!string.IsNullOrEmpty(_initialData))
            {
                _dataChunks = CreateDataChunks(_initialData);
                _totalChunks = _dataChunks.Length;
                // Important to clear initial data to avoid re-creating chunks on retries
                _initialData = "";
                _currentChunkIndex = 1;
                _targetPlayerIds = (string[])playerIds.Clone();
            }

            if (_currentChunkIndex == 1)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastDataTransferStarted));
            }

            SendDataChunk(_currentChunkIndex);
        }

        protected override void OnReadyCheckCompleted(string[] playerIds)
        {
            base.OnReadyCheckCompleted(playerIds);

            if (!IsProcessOwner()) return;

            if (_currentChunkIndex == _totalChunks)
            {
                ResetInternalTransferData();
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastDataTransferCompleted));
                return;
            }

            _currentChunkIndex++;
            StartReadyCheck(_targetPlayerIds);
        }

        protected override void OnReadyCheckCancelled()
        {
            base.OnReadyCheckCancelled();

            if (!IsProcessOwner()) return;

            ResetInternalTransferData();

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastDataTransferCancelled));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Sends string data to specified players.
        /// </summary>
        public virtual void TransferData(string data, string[] playerIds)
        {
            if (!ValidateMessage(data))
            {
                Debug.LogWarning("[TsvrcSender] Message validation failed. Transfer aborted.");
                return;
            }

            ResetInternalTransferData();
            _initialData = data;

            StartReadyCheck(playerIds);
        }

        /// <summary> 
        /// Cancels the current data transfer before completion.
        /// </summary>
        // public virtual void CancelDataTransfer()
        // {
        //     CancelReadyCheck();
        // }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when the data transfer is started.
        /// This method is intended to be called on the tracker players and the process owner.
        /// Make sure to invoke base.OnDataTransferStarted if overridden.
        /// </summary>
        protected virtual void OnDataTransferStarted() { }

        /// <summary>
        /// Called when the data transfer is completed.
        /// This method is intended to be called on the tracker players and the process owner.
        /// Make sure to invoke base.OnDataTransferCompleted if overridden.
        /// </summary>
        protected virtual void OnDataTransferCompleted() { }

        /// <summary>
        /// Called when the data transfer is cancelled.
        /// This method is intended to be called on the tracker players and the process owner.
        /// Make sure to invoke base.OnDataTransferCancelled if overridden.
        /// </summary>
        protected virtual void OnDataTransferCancelled() { }

        /// <summary>
        /// Called when a data chunk is sended.
        /// Only called on the owner of the sender.
        /// Make sure to invoke base.OnDataChunkSended if overridden.
        /// </summary>
        /// <param name="chunkIndex">The index of the chunk being sent (1-based).</param>
        protected virtual void OnSendDataChunkRequested(string dataChunk, int chunkIndex, int totalChunks) { }

        #endregion

        #region Network Events

        /// <summary>
        /// Network event to broadcast the start of data transfer.
        /// </summary>
        [NetworkCallable]
        public void BroadcastDataTransferStarted()
        {
            var playerId = TsPlayerUtils.GetPlayerID(Networking.LocalPlayer);
            if (!IsTrackedPlayer(playerId) && !IsProcessOwner()) return;

            OnDataTransferStarted();
        }

        /// <summary>
        /// Network event to broadcast the completion of data transfer.
        /// </summary>
        [NetworkCallable]
        public void BroadcastDataTransferCompleted()
        {
            var playerId = TsPlayerUtils.GetPlayerID(Networking.LocalPlayer);
            if (!IsTrackedPlayer(playerId) && !IsProcessOwner()) return;

            OnDataTransferCompleted();
        }

        /// <summary>
        /// Network event to broadcast the cancellation of data transfer.
        /// </summary>
        [NetworkCallable]
        public void BroadcastDataTransferCancelled()
        {
            var playerId = TsPlayerUtils.GetPlayerID(Networking.LocalPlayer);
            if (!IsTrackedPlayer(playerId) && !IsProcessOwner()) return;

            OnDataTransferCancelled();
        }

        #endregion

        #region Private Methods

        private void SendDataChunk(int chunkIndex)
        {
            string dataChunk = _dataChunks[chunkIndex - 1];
            OnSendDataChunkRequested(dataChunk, chunkIndex, _totalChunks);
        }

        private bool ValidateMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                Debug.LogWarning("[TsvrcSender] Cannot send empty message");
                return false;
            }

            if (message.Length > MAX_MESSAGE_SIZE)
            {
                Debug.LogError($"[TsvrcSender] Message too large: {message.Length} chars (max {MAX_MESSAGE_SIZE})");
                return false;
            }

            return true;
        }

        private void ResetInternalTransferData()
        {
            _initialData = "";
            _dataChunks = new string[0];
            _currentChunkIndex = 0;
            _totalChunks = 0;
            _targetPlayerIds = new string[0];
        }

        private string[] CreateDataChunks(string data)
        {
            var chunksCount = CalculateTotalChunks(data.Length);
            var chunks = new string[chunksCount];

            for (int i = 0; i < chunksCount; i++)
            {
                chunks[i] = ExtractChunk(data, i);
            }

            return chunks;
        }

        private int CalculateTotalChunks(int dataLength)
        {
            return (dataLength + CHUNK_SIZE - 1) / CHUNK_SIZE;
        }

        private string ExtractChunk(string data, int chunkIndex)
        {
            int startIndex = chunkIndex * CHUNK_SIZE;
            int length = System.Math.Min(CHUNK_SIZE, data.Length - startIndex);
            return data.Substring(startIndex, length);
        }

        #endregion
    }
}
