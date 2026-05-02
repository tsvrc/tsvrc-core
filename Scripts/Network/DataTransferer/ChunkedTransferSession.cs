using Tsvrc.Player;
using Tsvrc.Utils;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Network
{
    /// <summary>
    /// Orchestrates the sequential multi-chunk ready-check loop.
    /// Manages chunk-index state, inter-chunk gaps, player filtering, and the public
    /// <see cref="TransferData"/> / <see cref="CancelDataTransfer"/> API.
    /// Network broadcasts are delegated to virtual hooks overridden by <see cref="DataSender"/>.
    /// </summary>
    public class ChunkedTransferSession : DataChunker
    {
        private string _initialData = "";
        private string[] _dataChunks = new string[0];
        // Current chunk index (1-based)
        private int _currentChunkIndex = 0;
        private int _totalChunks = 0;

        private string[] _targetPlayerIds = new string[0];
        // True during the one-frame gap between SendCustomEventDelayedSeconds and _StartNextReadyCheck.
        // Prevents TransferData from overwriting mid-transfer state while _isRunning is temporarily false.
        private bool _pendingNextChunk = false;

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
                OnChunkSequenceStarted();

            // Guard: a subscriber to OnDataReceptionStartedEvent (fired inline above via
            // OnChunkSequenceStarted) may have called CancelDataTransfer(), resetting
            // _currentChunkIndex to 0. SendDataChunk(0) would access _dataChunks[-1].
            // GetTrackedPlayerIds() is called after this guard so that a cancel +
            // new TransferData() inside the callback uses the new transfer's player list.
            if (!IsProcessRunning()) return;

            SendDataChunk(_currentChunkIndex, GetTrackedPlayerIds());
        }

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();
            // Guard: base.OnProcessStopped() fires TsEmit(OnReadyCheckStoppedEvent) inline.
            // A subscriber calling TransferData() from that event starts a new transfer (_isRunning=true).
            // Calling OnChunkSequenceStopped now would clear _transferActive on all clients.
            if (IsProcessRunning()) return;
            OnChunkSequenceStopped();
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
                OnChunkSequenceCompleted();
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

            // Filter players who departed or suspended during the inter-chunk gap.
            // Both PlayerTracker.OnPlayerLeft and PlayerTracker.OnPlayerSuspendChanged guard
            // with IsProcessRunning()=true, so neither fires while _isRunning=false between
            // chunks. A departed player has left the instance; a suspended player cannot call
            // SetReady() or respond to network events (VRChat docs: "While suspended, devices
            // don't run Udon code or respond to network events until the player reopens VRChat").
            // Either case would stall CheckAllPlayersReady() indefinitely.
            // Using GetAllPlayers() instead of GetAllPlayerIDs() lets us check isSuspended in
            // the same pass, avoiding an O(n) FindPlayerByID call per tracked player.
            VRCPlayerApi[] allPlayers = TsPlayer.GetAllPlayers();
            string[] activeNonSuspendedIds = new string[allPlayers.Length];
            int activeCount = 0;
            for (int i = 0; i < allPlayers.Length; i++)
            {
                if (!allPlayers[i].isSuspended)
                    activeNonSuspendedIds[activeCount++] = TsPlayer.GetPlayerID(allPlayers[i]);
            }
            if (activeCount < allPlayers.Length)
            {
                string[] trimmed = new string[activeCount];
                System.Array.Copy(activeNonSuspendedIds, trimmed, activeCount);
                activeNonSuspendedIds = trimmed;
            }

            int count = 0;
            for (int i = 0; i < _targetPlayerIds.Length; i++)
            {
                if (TsArray.Contains(activeNonSuspendedIds, _targetPlayerIds[i]))
                    count++;
            }

            if (count == 0)
            {
                // All targets departed or suspended during the gap. Cancel gracefully so all
                // clients receive the stopped event and clean up receiver state.
                ResetInternalTransferData();
                OnChunkSequenceStopped();
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
                    if (TsArray.Contains(activeNonSuspendedIds, _targetPlayerIds[i]))
                        filteredIds[idx++] = _targetPlayerIds[i];
                }
            }

            StartReadyCheck(filteredIds);
        }

        protected override void OnProcessUpdate()
        {
            base.OnProcessUpdate(); // ReadyCheckProcess → CheckAllPlayersReady

            // CheckAllPlayersReady() guards: if (trackedPlayerIds.Length == 0) return.
            // This means that if all tracked players depart or suspend DURING an active ready
            // check, base.OnProcessUpdate() becomes a no-op — the process never stops or
            // completes, and the transfer stalls indefinitely. The between-chunk gap is handled
            // by _StartNextReadyCheck filtering departed+suspended players, but there is no
            // equivalent filter while a ready check is actively running.
            //
            // Detect the empty-tracker case here and cancel. IsProcessRunning() guards against
            // the case where base.OnProcessUpdate() just completed the process (all remaining
            // players were ready), in which case _isRunning is already false and CancelDataTransfer
            // must not fire.
            if (IsProcessRunning() && GetTrackedPlayerIds().Length == 0)
                CancelDataTransfer();
        }

        protected override void OnOwnerAbandonedProcess()
        {
            // Transfer state (_dataChunks, _currentChunkIndex, _totalChunks) is owner-only and
            // unsynced. The new owner has all these at default (0/empty). Without stopping first,
            // base.OnOwnerAbandonedProcess could reach CheckAllPlayersReady and fire a false completion
            // (_currentChunkIndex == _totalChunks == 0). IsProcessOwner() returns true here because
            // TakeOverAbandonedProcess already set _ownerId before calling this method.
            //
            // Guard: if the previous owner left during the inter-chunk one-frame gap
            // (_pendingNextChunk=true on the old client), _isRunning was already set false by
            // InternalCleanup when the previous chunk's ready-check completed. Calling StopReadyCheck
            // → StopProcess() with _isRunning=false triggers a spurious "[TsvrcProcess] Process is
            // not running" warning even though the state is correct. Skip the call in that case:
            // base.OnOwnerAbandonedProcess() is already a no-op there because InternalCleanup also
            // cleared _trackedPlayerIds, causing PlayerTracker.OnOwnerAbandonedProcess to return
            // immediately on the _trackedPlayerIds.Length == 0 check.
            if (IsProcessRunning()) StopReadyCheck();
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
                OnChunkSequenceStopped();
                return;
            }
            StopReadyCheck();
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
        /// Sends a data chunk at the specified index.
        /// </summary>
        protected void SendDataChunk(int chunkIndex, string[] playerIds)
        {
            string dataChunk = _dataChunks[chunkIndex - 1];
            OnDataChunkSendRequested(dataChunk, chunkIndex, _totalChunks, playerIds);
        }

        /// <summary>
        /// Called when the first chunk of a new sequence is about to be sent (chunk index == 1).
        /// Override to broadcast the transfer-started event to all clients.
        /// Only invoked on the process owner.
        /// </summary>
        protected virtual void OnChunkSequenceStarted() { }

        /// <summary>
        /// Called when the transfer is cancelled or all targets depart before completion.
        /// Override to broadcast the transfer-stopped event to all clients.
        /// Only invoked on the process owner.
        /// </summary>
        protected virtual void OnChunkSequenceStopped() { }

        /// <summary>
        /// Called when the last chunk's ready-check completes successfully.
        /// Override to broadcast the transfer-completed event to all clients.
        /// Only invoked on the process owner.
        /// </summary>
        protected virtual void OnChunkSequenceCompleted() { }

        /// <summary>
        /// Called when a data chunk is ready to be sent.
        /// Only invoked on the process owner. Fires for each chunk.
        /// </summary>
        protected virtual void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds) { }
    }
}
