using Tsvrc.Player;
using Tsvrc.Process;
using Tsvrc.Utils;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
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

        // Deferred-emission flags. TsEmit fires synchronously while ExecuteStop/ExecuteComplete
        // is still on the call stack (InternalCleanup not yet run). Deferring to the next event
        // cycle ensures InternalCleanup completes before user callbacks fire.
        // Not reset by ResetInternalTransferData(). They must survive until _EmitDataTransfer*
        // fires. Cleared only by NotifyTrackedPlayersDataTransferStarted to suppress stale emits.
        private bool _pendingTransferStopped = false;
        private bool _pendingTransferCompleted = false;

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

            if (_currentChunkIndex == 1)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStarted));
            }

            // Guard: a subscriber to OnDataReceptionStartedEvent (fired inline above) may have called
            // CancelDataTransfer(), resetting _currentChunkIndex to 0. SendDataChunk(0) would access
            // _dataChunks[-1]. GetTrackedPlayerIds() is called after this guard so that a cancel +
            // new TransferData() inside the callback uses the new transfer's player list.
            if (!IsProcessRunning()) return;

            SendDataChunk(_currentChunkIndex, GetTrackedPlayerIds());
        }

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();
            // Guard: base.OnProcessStopped() fires TsEmit(OnReadyCheckStoppedEvent) inline.
            // A subscriber calling TransferData() from that event starts a new transfer (_isRunning=true).
            // Sending the stopped broadcast now would clear _transferActive on all clients.
            if (IsProcessRunning()) return;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStopped));
        }

        protected override void OnProcessCompleted()
        {
            // Capture before base call: base.OnProcessCompleted() fires TsEmit(OnReadyCheckCompletedEvent)
            // inline, and a subscriber calling TransferData() may reset _currentChunkIndex/_totalChunks.
            bool isLastChunk = _currentChunkIndex == _totalChunks;

            base.OnProcessCompleted();

            // Guard: same as OnProcessStopped. A new transfer from an inline subscriber may have
            // set _isRunning=true. Proceeding would corrupt the new transfer (wrong completion
            // broadcast on last chunk, or corrupted _targetPlayerIds on non-last chunk).
            if (IsProcessRunning()) return;

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

            // Guard: a TransferData() call from inside OnProcessCompleted/Stopped's inline callbacks
            // may have started a new transfer (_isRunning=true). Avoid advancing _currentChunkIndex
            // or resetting the new transfer's chunk state.
            if (IsProcessRunning()) return;

            // Only continue to next chunk if completed successfully (not stopped)
            if (isCompleted && _currentChunkIndex < _totalChunks)
            {
                _currentChunkIndex++;
                // Flag the gap so TransferData/CancelDataTransfer behave correctly while
                // _isRunning is temporarily false before the next chunk starts.
                _pendingNextChunk = true;
                // Defer to the next frame so the current _TickProcessUpdate exits cleanly
                // before the new process starts its own loop, preventing duplicate concurrent
                // tick loops accumulating with each chunk.
                SendCustomEventDelayedSeconds(nameof(_StartNextReadyCheck), 0f);
            }
            else
            {
                ResetInternalTransferData();
            }
        }

        public void _StartNextReadyCheck()
        {
            // Guard: CancelDataTransfer() may have cleared _pendingNextChunk during the one-frame gap.
            // Without this, a cancelled transfer would call StartReadyCheck with reset state.
            if (!_pendingNextChunk) return;

            _pendingNextChunk = false;

            // Filter players who departed during the inter-chunk gap. PlayerTracker.OnPlayerLeft
            // ignores departures while _isRunning=false, so departed players would remain in
            // _targetPlayerIds and stall CheckAllPlayersReady() indefinitely.
            string[] activeIds = TsPlayer.GetAllPlayerIDs();

            int count = 0;
            for (int i = 0; i < _targetPlayerIds.Length; i++)
            {
                if (TsArray.Contains(activeIds, _targetPlayerIds[i]))
                    count++;
            }

            if (count == 0)
            {
                // All targets departed during the gap. Cancel gracefully so all clients
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

        protected override void OnOwnerAbandonedProcess()
        {
            // Transfer state (_dataChunks, _currentChunkIndex, _totalChunks) is owner-only and
            // unsynced. The new owner has all these at default (0/empty). Without stopping first,
            // base.OnOwnerAbandonedProcess could reach CheckAllPlayersReady and fire a false completion
            // (_currentChunkIndex == _totalChunks == 0). IsProcessOwner() returns true here because
            // TakeOverAbandonedProcess already set _ownerId before calling this method.
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

            // Guard: an empty player list leaves the process with no path to completion.
            // CheckAllPlayersReady never returns true and _TickProcessUpdate runs forever.
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
                // The process is stopped between chunks, so cancel the pending continuation.
                ResetInternalTransferData(); // also clears _pendingNextChunk
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NotifyTrackedPlayersDataTransferStopped));
                return;
            }
            StopReadyCheck();
        }

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
            // _pendingTransferStopped and _pendingTransferCompleted are intentionally NOT cleared
            // here. See their field declarations for the full explanation.
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

        /// <summary>
        /// Called when a data chunk is ready to be sent.
        /// Only invoked on the process owner.
        /// This fires for each chunk.
        /// </summary>
        protected virtual void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds) { }

        /// <summary>Called on all clients when the data transfer starts.</summary>
        protected virtual void OnTransferStarted() { }
        /// <summary>Called on all clients when the data transfer is stopped before completion.</summary>
        protected virtual void OnTransferStopped() { }
        /// <summary>Called on all clients when the data transfer completes successfully.</summary>
        protected virtual void OnTransferCompleted() { }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer starts.
        /// </summary>
        // maxEventsPerSecond: 100. Must match BroadcastDataChunkReceived (also 100/s) so that
        // per VRChat docs (creators.vrchat.com/worlds/udon/networking/events#rate-limiting):
        // "The order in which events are sent and received is guaranteed as long as you don't
        // hit your own defined rate-limit." At 5/s (default), rapid CancelDataTransfer+TransferData
        // cycles (>5/s) queue this event while the 100/s chunk event drains ahead of it. Remote
        // clients then receive BroadcastDataChunkReceived before NotifyTrackedPlayersDataTransferStarted,
        // so _transferActive is still false when the first chunk arrives — the chunk is dropped,
        // the recipient never calls SetReady(), and the transfer stalls indefinitely.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferStarted()
        {
            // Cancel any deferred stopped/completed emit from a previous transfer so it
            // does not fire after this new transfer has already started.
            _pendingTransferStopped = false;
            _pendingTransferCompleted = false;
            OnTransferStarted();
            TsEmit(OnDataTransferStartedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer is stopped.
        /// </summary>
        // maxEventsPerSecond: 100. Same rationale as NotifyTrackedPlayersDataTransferStarted:
        // must match BroadcastDataChunkReceived to preserve event ordering under load.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferStopped()
        {
            // Set flag BEFORE the virtual callback. OnTransferStopped() fires synchronously, and
            // a subclass may call TransferData() inside it. That triggers
            // NotifyTrackedPlayersDataTransferStarted inline, clearing this flag. Setting after
            // the callback would re-set it after that clear, causing a spurious stopped event.
            _pendingTransferStopped = true;
            OnTransferStopped();
            // Defer TsEmit: on the owner this fires while ExecuteStop is still on the call stack
            // (InternalCleanup not yet run), so a TransferData() from the callback would corrupt state.
            SendCustomEventDelayedSeconds(nameof(_EmitDataTransferStopped), 0f);
        }

        // Underscore prefix: local-only, cannot be triggered via network event.
        // Called by SendCustomEventDelayedSeconds in NotifyTrackedPlayersDataTransferStopped.
        public void _EmitDataTransferStopped()
        {
            // Guard: NotifyTrackedPlayersDataTransferStarted clears _pendingTransferStopped,
            // suppressing this if a new transfer started before the deferred call fires.
            if (!_pendingTransferStopped) return;
            _pendingTransferStopped = false;
            TsEmit(OnDataTransferStoppedEvent);
        }

        /// <summary>
        /// Broadcast target: fires on all instance players when the data transfer completes.
        /// </summary>
        // maxEventsPerSecond: 100. Same rationale as NotifyTrackedPlayersDataTransferStarted.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void NotifyTrackedPlayersDataTransferCompleted()
        {
            // Flag before virtual call: a new TransferData() from the callback would call
            // NotifyTrackedPlayersDataTransferStarted inline, clearing this flag; setting
            // after the call would re-set it, causing a spurious completed emit.
            _pendingTransferCompleted = true;
            OnTransferCompleted();
            // Defer so InternalCleanup finishes before user callbacks run
            // (same reason as NotifyTrackedPlayersDataTransferStopped).
            SendCustomEventDelayedSeconds(nameof(_EmitDataTransferCompleted), 0f);
        }

        // Underscore prefix: local-only, cannot be triggered via network event.
        // Called by SendCustomEventDelayedSeconds in NotifyTrackedPlayersDataTransferCompleted.
        public void _EmitDataTransferCompleted()
        {
            // Guard: NotifyTrackedPlayersDataTransferStarted clears _pendingTransferCompleted,
            // suppressing this if a new transfer started before the deferred call fires.
            if (!_pendingTransferCompleted) return;
            _pendingTransferCompleted = false;
            TsEmit(OnDataTransferCompletedEvent);
        }

    }
}
