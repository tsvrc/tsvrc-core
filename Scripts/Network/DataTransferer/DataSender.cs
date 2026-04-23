using Tsvrc.Player;
using Tsvrc.Process;
using Tsvrc.Utils;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    public class DataSender : ReadyCheckProcess
    {
        /// <summary>
        /// Emitted when the data transfer starts.
        /// No <c>Last*</c> properties are updated.
        /// </summary>
        public const string OnDataTransferStartedEvent = "OnDataTransferStarted";
        /// <summary>
        /// Emitted when the data transfer is stopped before completion.
        /// No <c>Last*</c> properties are updated.
        /// </summary>
        public const string OnDataTransferStoppedEvent = "OnDataTransferStopped";
        /// <summary>
        /// Emitted when the data transfer completes.
        /// No <c>Last*</c> properties are updated.
        /// </summary>
        public const string OnDataTransferCompletedEvent = "OnDataTransferCompleted";

        protected const int CHUNK_SIZE = 15000;
        protected const int MAX_MESSAGE_SIZE = 500000;

        private string _initialData = "";
        private string[] _dataChunks = new string[0];
        // Current chunk index (1-based)
        private int _currentChunkIndex = 0;
        private int _totalChunks = 0;

        private string[] _targetPlayerIds = new string[0];
        // True during the one-frame gap between SendCustomEventDelayedSeconds and _StartNextReadyCheck.
        // Prevents TransferData from overwriting mid-transfer state while _isRunning is temporarily false.
        private bool _pendingNextChunk = false;

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

            var trackedPlayerIds = GetTrackedPlayerIds();

            if (_currentChunkIndex == 1)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStarted));
            }

            // Guard: a subscriber to OnDataReceptionStartedEvent (fired inline from the network event
            // above) may have called CancelDataTransfer(), which runs StopReadyCheck() →
            // ExecuteStop() → InternalCleanup() → ResetInternalTransferData(), resetting
            // _currentChunkIndex to 0. SendDataChunk(0, ...) would access _dataChunks[0-1] = _dataChunks[-1]
            // → IndexOutOfRangeException. Source: creators.vrchat.com/worlds/udon/networking/events —
            // "trigger locally before moving on, just like a regular function call would".
            if (!IsProcessRunning()) return;

            SendDataChunk(_currentChunkIndex, trackedPlayerIds);
        }

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStopped));
        }

        protected override void OnProcessCompleted()
        {
            base.OnProcessCompleted();

            bool isLastChunk = _currentChunkIndex == _totalChunks;

            if (isLastChunk)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferCompleted));
                return;
            }

            // PlayerTracker.OnProcessCleanup reassigns _trackedPlayerIds to a new empty array.
            // Capturing the reference here keeps the player list alive for the next chunk.
            _targetPlayerIds = GetTrackedPlayerIds();
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            // Only continue to next chunk if completed successfully (not stopped)
            if (isCompleted && _currentChunkIndex < _totalChunks)
            {
                _currentChunkIndex++;
                // Flag the gap so TransferData/CancelDataTransfer behave correctly while
                // _isRunning is temporarily false before the next chunk starts.
                _pendingNextChunk = true;
                // Defer to the next frame so the current _TickProcessUpdate exits cleanly
                // before the new process starts its own loop — prevents duplicate concurrent
                // tick loops accumulating with each chunk.
                SendCustomEventDelayedSeconds(nameof(_StartNextReadyCheck), 0);
            }
            else
            {
                ResetInternalTransferData();
            }
        }

        public void _StartNextReadyCheck()
        {
            // Guard: CancelDataTransfer() may have cleared _pendingNextChunk during the one-frame
            // deferral window. Without this check, a cancelled transfer would still call
            // StartReadyCheck with empty/reset state, causing SendDataChunk(0) → _dataChunks[-1] crash.
            if (!_pendingNextChunk) return;

            _pendingNextChunk = false;

            // Filter departed players from _targetPlayerIds before starting the next chunk.
            // PlayerTracker.OnPlayerLeft guards on IsProcessRunning() and therefore ignores any
            // player departure that happens while _isRunning=false (the inter-chunk gap). A player
            // who left during that window stays in _targetPlayerIds, gets tracked in the next
            // StartReadyCheck, and can never call SetReady() — CheckAllPlayersReady() then stalls
            // indefinitely. The same scan pattern is already used in OnOwnerAbandonedProcess.
            string[] activeIds = TsPlayer.GetAllPlayerIDs();

            int count = 0;
            for (int i = 0; i < _targetPlayerIds.Length; i++)
            {
                if (TsArray.Contains(activeIds, _targetPlayerIds[i]))
                    count++;
            }

            if (count == 0)
            {
                // All targets departed during the gap — cancel gracefully so all clients
                // receive the stopped event and clean up receiver state.
                ResetInternalTransferData();
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStopped));
                return;
            }

            string[] filteredIds;
            if (count == _targetPlayerIds.Length)
            {
                filteredIds = _targetPlayerIds;
            }
            else
            {
                filteredIds = new string[count];
                int idx = 0;
                for (int i = 0; i < _targetPlayerIds.Length; i++)
                {
                    if (TsArray.Contains(activeIds, _targetPlayerIds[i]))
                        filteredIds[idx++] = _targetPlayerIds[i];
                }
            }

            StartReadyCheck(filteredIds);
        }

        #endregion

        #region Public Methods

        protected override void OnOwnerAbandonedProcess()
        {
            // Transfer state (_dataChunks, _currentChunkIndex, _totalChunks) is unsynced and
            // only exists on the original owner. The new owner has all these at default (0/empty).
            // Without this guard: base.OnOwnerAbandonedProcess calls BroadcastRemoveTrackedPlayers
            // for the departed owner, which triggers _OnTrackingPlayersRemoved → CheckAllPlayersReady.
            // If synced _readyPlayerIds still shows remaining players as ready, CompleteReadyCheck
            // fires → OnProcessCompleted sees _currentChunkIndex==_totalChunks (0==0) → emits
            // NotifyTrackedPlayersDataTransferCompleted as a false completion.
            // Stopping first sends the correct stopped broadcast and prevents the false completion.
            // IsProcessOwner() returns true here because TakeOverAbandonedProcess set _ownerId
            // before calling this method, so StopProcess() executes ExecuteStop() directly.
            StopReadyCheck();
            base.OnOwnerAbandonedProcess();
        }

        /// <summary>
        /// Sends string data to specified players.
        /// </summary>
        public virtual void TransferData(string data, string[] playerIds)
        {
            if (IsProcessRunning() || _pendingNextChunk)
            {
                Debug.LogWarning("[TsvrcDataSender] Transfer already in progress. Call CancelDataTransfer() first.");
                return;
            }

            // Guard: ReadyCheckProcess.CheckAllPlayersReady returns early when trackedPlayerIds
            // is empty, and BroadcastAddReadyPlayer rejects non-tracked callers, so no player
            // can ever signal ready. The _TickProcessUpdate loop would run forever with no path
            // to completion — a permanent resource leak until CancelDataTransfer is called.
            if (playerIds == null || playerIds.Length == 0)
            {
                Debug.LogWarning("[TsvrcDataSender] Cannot transfer to null or empty player list.");
                return;
            }

            if (!ValidateMessage(data)) return;

            ResetInternalTransferData();
            _initialData = data;

            StartReadyCheck(playerIds);
        }

        /// <summary>
        /// Cancels the current data transfer before completion.
        /// </summary>
        public virtual void CancelDataTransfer()
        {
            if (_pendingNextChunk)
            {
                // Process is stopped between chunks — cancel the pending continuation.
                ResetInternalTransferData(); // also clears _pendingNextChunk
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStopped));
                return;
            }
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
            _pendingNextChunk = false;
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
        /// Broadcast target: fires on all instance players when the data transfer starts.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersDataTransferStarted()
        {
            TsEmit(OnDataTransferStartedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer is stopped.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersDataTransferStopped()
        {
            TsEmit(OnDataTransferStoppedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer completes.
        /// </summary>
        [NetworkCallable]
        public void NotifyTrackedPlayersDataTransferCompleted()
        {
            TsEmit(OnDataTransferCompletedEvent);
        }

        #endregion
    }
}
