using Tsvrc.Player;
using Tsvrc.Utils;
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

        #region TsvrcProcess Callbacks

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            if (!string.IsNullOrEmpty(_initialData))
            {
                _dataChunks = CreateDataChunks(_initialData);
                _totalChunks = _dataChunks.Length;
                // Important to clear initial data to avoid re-creating chunks on retries
                _initialData = "";
                _currentChunkIndex = 1;
            }

            var trackedPlayerIds = (string[])GetTrackedPlayerIds().Clone();

            if (_currentChunkIndex == 1)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStarted), trackedPlayerIds);
            }

            SendDataChunk(_currentChunkIndex, trackedPlayerIds);
        }

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();

            var stoppedPlayerIds = (string[])GetTrackedPlayerIds().Clone();
            ResetInternalTransferData();

            if (stoppedPlayerIds.Length > 0)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStopped), stoppedPlayerIds);
            }
        }

        protected override void OnProcessCompleted()
        {
            base.OnProcessCompleted();

            var completedPlayerIds = (string[])GetTrackedPlayerIds().Clone();

            // Check if this was the LAST chunk BEFORE incrementing
            bool isLastChunk = _currentChunkIndex == _totalChunks;

            if (isLastChunk)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferCompleted), completedPlayerIds);
                return;
            }

            // Not the last chunk - save target players for next chunk
            _targetPlayerIds = completedPlayerIds;
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            // Only continue to next chunk if completed successfully (not stopped)
            if (isCompleted && _currentChunkIndex < _totalChunks)
            {
                _currentChunkIndex++;
                StartReadyCheck(_targetPlayerIds);
            }
            else
            {
                ResetInternalTransferData();
            }
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
                Debug.LogWarning("[TsvrcDataSender] Message validation failed. Transfer aborted.");
                return;
            }

            ResetInternalTransferData();
            _initialData = data;

            StartReadyCheck(playerIds);
        }

        /// <summary>
        /// Cancels the current data transfer before completion.
        /// </summary>
        public virtual void CancelDataTransfer()
        {
            StopReadyCheck();
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Checks if a message is valid for transfer.
        /// </summary>
        protected bool ValidateMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                Debug.LogWarning("[TsvrcDataSender] Cannot send empty message");
                return false;
            }

            if (message.Length > MAX_MESSAGE_SIZE)
            {
                Debug.LogError($"[TsvrcDataSender] Message too large: {message.Length} chars (max {MAX_MESSAGE_SIZE})");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resets internal data transfer state.
        /// </summary>
        protected void ResetInternalTransferData()
        {
            _initialData = "";
            _dataChunks = new string[0];
            _currentChunkIndex = 0;
            _totalChunks = 0;
            _targetPlayerIds = new string[0];
        }

        /// <summary>
        /// Creates data chunks from the full data string.
        /// </summary>
        protected string[] CreateDataChunks(string data)
        {
            var chunksCount = CalculateTotalChunks(data.Length);
            var chunks = new string[chunksCount];

            for (int i = 0; i < chunksCount; i++)
            {
                chunks[i] = ExtractChunk(data, i);
            }

            return chunks;
        }

        /// <summary>
        /// Calculates the total number of chunks needed for the data length.
        /// </summary>
        protected int CalculateTotalChunks(int dataLength)
        {
            return (dataLength + CHUNK_SIZE - 1) / CHUNK_SIZE;
        }

        /// <summary>
        /// Extracts a specific chunk from the data string.
        /// </summary>
        protected string ExtractChunk(string data, int chunkIndex)
        {
            int startIndex = chunkIndex * CHUNK_SIZE;
            int length = System.Math.Min(CHUNK_SIZE, data.Length - startIndex);
            return data.Substring(startIndex, length);
        }

        /// <summary>
        /// Sends a data chunk at the specified index.
        /// </summary>
        protected void SendDataChunk(int chunkIndex, string[] playerIds)
        {
            string dataChunk = _dataChunks[chunkIndex - 1];
            OnDataChunkSendRequested(dataChunk, chunkIndex, _totalChunks, playerIds);
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when the data transfer starts on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// This fires once at the beginning of the transfer (first chunk).
        /// For owner-only logic, override OnProcessStarted() from the base class.
        /// </summary>
        protected virtual void OnDataTransferStartedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when the data transfer is stopped on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// For owner-only logic, override OnProcessStopped() from the base class.
        /// </summary>
        protected virtual void OnDataTransferStoppedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when the data transfer completes on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// This fires once at the end when all chunks are complete.
        /// For owner-only logic, override OnProcessCompleted() from the base class.
        /// </summary>
        protected virtual void OnDataTransferCompletedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when a data chunk is ready to be sent.
        /// Only invoked on the process owner.
        /// This fires for each chunk.
        /// </summary>
        /// <param name="dataChunk">The chunk of data to send.</param>
        /// <param name="chunkIndex">The index of the chunk being sent (1-based).</param>
        /// <param name="totalChunks">The total number of chunks in the transfer.</param>
        /// <param name="playerIds">The player IDs for this transfer.</param>
        protected virtual void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds) { }

        #endregion

        #region Network Events

        /// <summary>
        /// Network callable method to notify tracked players that the data transfer has started.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersDataTransferStarted(string[] playerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(playerIds, playerId)) return;

            OnDataTransferStartedAsTrackedPlayer(playerIds);
        }

        /// <summary>
        /// Network callable method to notify tracked players that the data transfer has stopped.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersDataTransferStopped(string[] playerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(playerIds, playerId)) return;

            OnDataTransferStoppedAsTrackedPlayer(playerIds);
        }

        /// <summary>
        /// Network callable method to notify tracked players that the data transfer has completed.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersDataTransferCompleted(string[] playerIds)
        {
            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(playerIds, playerId)) return;

            OnDataTransferCompletedAsTrackedPlayer(playerIds);
        }

        #endregion
    }
}
