using Tsvrc.Player;
using Tsvrc.Utils;
using UdonSharp;
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
        // Synced so that if the owner leaves during this gap, the new owner can detect it in
        // OnOwnerAbandonedProcess and broadcast the stopped event to clean up receiver state.
        [UdonSynced] private bool _pendingNextChunk = false;

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            if (!string.IsNullOrEmpty(_initialData))
            {
                _dataChunks = CreateDataChunks(_initialData);
                _totalChunks = _dataChunks.Length;
                // Clear now so _initialData does not survive a retry of OnProcessStarted.
                _initialData = "";
                _currentChunkIndex = 1;
            }

            if (_currentChunkIndex == 1)
                OnChunkSequenceStarted();

            // A subscriber to OnDataReceptionStartedEvent (fired inline by OnChunkSequenceStarted)
            // may have called CancelDataTransfer(), resetting _currentChunkIndex to 0.
            // SendDataChunk(0) would access _dataChunks[-1]. GetTrackedPlayerIds() is called
            // after this check so a cancel + new TransferData() in the callback uses the
            // new transfer's player list.
            if (!IsProcessRunning()) return;

            SendDataChunk(_currentChunkIndex, GetTrackedPlayerIds());
        }

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();
            // base.OnProcessStopped() fires TsEmit(OnReadyCheckStoppedEvent) inline, and a
            // subscriber may call TransferData() from there, starting a new transfer.
            // Calling OnChunkSequenceStopped here would clear _transferActive on all clients.
            if (IsProcessRunning()) return;
            OnChunkSequenceStopped();
        }

        protected override void OnProcessCompleted()
        {
            // Capture before the base call because base.OnProcessCompleted() fires
            // TsEmit(OnReadyCheckCompletedEvent) inline, and a subscriber calling TransferData()
            // may reset _currentChunkIndex and _totalChunks.
            bool isLastChunk = _currentChunkIndex == _totalChunks;

            base.OnProcessCompleted();

            // Same guard as OnProcessStopped: a subscriber may have started a new transfer
            // inline, setting _isRunning=true. Proceeding would corrupt that transfer.
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

            // A TransferData() call from an inline callback in OnProcessCompleted or OnProcessStopped
            // may have started a new transfer. Do not advance _currentChunkIndex or reset chunk state.
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
            // CancelDataTransfer() may have cleared _pendingNextChunk during the one-frame gap.
            // Without this check, a cancelled transfer would call StartReadyCheck with reset state.
            if (!_pendingNextChunk) return;

            _pendingNextChunk = false;

            // PlayerTracker.OnPlayerLeft and OnPlayerSuspendChanged both guard with
            // IsProcessRunning()=true, so neither fires while _isRunning=false between chunks.
            // Departed and suspended players must be filtered manually before starting the next chunk.
            // A suspended player cannot respond to network events (VRChat docs: "While suspended,
            // devices don't run Udon code or respond to network events until the player reopens VRChat").
            // Either case would stall CheckAllPlayersReady() indefinitely.
            // GetAllPlayers() is used so the isSuspended check and ID lookup happen in a single pass.
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
                // _pendingNextChunk is [UdonSynced] and was cleared at the top of this method
                // outside InternalCleanup, so serialize explicitly.
                RequestSerialization();
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

            // CheckAllPlayersReady() returns early when trackedPlayerIds is empty, so if all
            // tracked players leave or suspend during an active ready check, the process stalls.
            // _StartNextReadyCheck filters these players between chunks, but there is no
            // equivalent during an active ready check, so we detect and cancel here.
            // IsProcessRunning() prevents calling CancelDataTransfer() when base.OnProcessUpdate()
            // just completed the process (all players were ready), where _isRunning is already false.
            if (IsProcessRunning() && GetTrackedPlayerIds().Length == 0)
                CancelDataTransfer();
        }

        protected override void OnOwnerAbandonedProcess()
        {
            // Transfer state (_dataChunks, _currentChunkIndex, _totalChunks) is owner-only and
            // unsynced, so the new owner starts with all of these at zero or empty. Without stopping
            // first, a stale tick could reach CheckAllPlayersReady and false-complete the process
            // because _currentChunkIndex == _totalChunks == 0. IsProcessOwner() is already true here
            // because TakeOverAbandonedProcess set _ownerId before calling this method.
            //
            // If the previous owner left during an active ready check, stop it.
            //
            // If the previous owner left during the inter-chunk gap (_pendingNextChunk=true),
            // _isRunning is already false. The deferred _StartNextReadyCheck scheduled on the old
            // owner is lost when ownership transfers. Without the else-if branch, no DataTransferStopped
            // event would ever be broadcast, leaving all receivers with _transferActive=true.
            // _pendingNextChunk is synced, so the new owner reads the correct value here.
            //
            // The stale sync packet from the old owner can arrive AFTER this method runs, in
            // which case _pendingNextChunk is still false here. OnDeserialization handles that.
            if (IsProcessRunning())
                StopReadyCheck();
            else if (_pendingNextChunk)
            {
                // Broadcast stopped to clean up _transferActive on all receivers, then serialize
                // the cleared _pendingNextChunk so late joiners do not see a stale true.
                ResetInternalTransferData();
                OnChunkSequenceStopped();
                RequestSerialization();
            }
            base.OnOwnerAbandonedProcess();
        }

        public override void OnDeserialization()
        {
            base.OnDeserialization();

            // Late-packet gap case:
            // The old owner's sync packet (chunk N InternalCleanup: _pendingNextChunk=true,
            // _isRunning=false) can arrive AFTER OnOwnerAbandonedProcess already ran and saw
            // _pendingNextChunk=false. No stopped event was broadcast in that window.
            //
            // Networking.IsOwner() is used instead of IsProcessOwner() because the stale packet
            // may overwrite _ownerPlayerIdInt=0 (cleared by the old owner's InternalCleanup),
            // making IsProcessOwner() return false even though we hold Unity ownership.
            // TsvrcProcess.OnDeserialization only recovers stale-packet ownership when _isRunning=true,
            // so it does not handle the _pendingNextChunk=true, _isRunning=false case.
            //
            // If a new transfer already started (IsProcessRunning()=true), the started event
            // already reset receiver state, so we do not broadcast stopped again.
            //
            // The _dataChunks.Length == 0 guard prevents a false positive when packet A arrives
            // after a new transfer started from a deferred user callback (e.g. OnDataTransferStopped
            // next-frame). Packet A overwrites _isRunning=false before this method runs.
            // TsvrcProcess.OnDeserialization hits the early return on _ownerId="" and does not
            // reassert ownership. Without this guard, the handler would wipe the live transfer's
            // _dataChunks and broadcast a spurious stopped event. _dataChunks is unsynced and
            // only populated in OnProcessStarted, so it is always non-empty for a live transfer.
            if (Networking.IsOwner(gameObject) && !IsProcessRunning() && _pendingNextChunk
                && _dataChunks.Length == 0)
            {
                ResetInternalTransferData(); // clears _pendingNextChunk = false
                OnChunkSequenceStopped();
                RequestSerialization(); // serialize _pendingNextChunk = false to all clients
                return;
            }

            // Zombie-process case:
            // The old owner may send two sync packets when leaving during chunk N+1's first frame:
            //   Packet A (frame N,   InternalCleanup): _isRunning=false, _pendingNextChunk=true
            //   Packet B (frame N+1, StartReadyCheck): _isRunning=true,  _pendingNextChunk=false
            //
            // If both arrive after TakeOverAbandonedProcess, TsvrcProcess.OnDeserialization sees
            // packet B's _isRunning=true with a departed _ownerId, calls SetProcessOwner(local)
            // and restarts the tick loop. The new owner now has a zombie process:
            //   _isRunning=true, IsProcessOwner()=true, _dataChunks.Length=0 (unsynced)
            //   _trackedPlayerIds = stale players from packet B
            //
            // A tick fires CheckAllPlayersReady(). Those stale players already called SetReady(),
            // so it immediately false-completes. OnProcessCompleted sees isLastChunk=(0==0)=true
            // and calls OnChunkSequenceCompleted, broadcasting a false transfer-complete.
            //
            // _dataChunks is unsynced and always populated synchronously in OnProcessStarted
            // before RequestSerialization is called. It is never empty on a legitimate owner
            // with _isRunning=true. OnDeserialization never fires for the sender of
            // RequestSerialization (VRChat guarantee), so this check never produces a false positive.
            //
            // The second StopReadyCheck() is NECESSARY, not merely idempotent.
            // SetProcessOwner() in TsvrcProcess.OnDeserialization calls RequestSerialization()
            // while _isRunning is still true, re-broadcasting it to all remote clients. Their
            // OnDeserialization restores _readyCheckActive=true. Without the second stop,
            // those clients stay stuck with _readyCheckActive=true permanently.
            //
            // As a result, NotifyTrackedPlayersProcessStopped fires twice on all clients.
            // The second fire is what clears _readyCheckActive on clients where it was restored.
            // NotifyTrackedPlayersDataTransferStopped also fires twice, but the _pendingTransferStopped
            // and _pendingStop guards in DataSender and DataSenderReceiver suppress the second
            // user-visible emission.
            if (IsProcessOwner() && IsProcessRunning() && _dataChunks.Length == 0 && !_pendingNextChunk)
                StopReadyCheck();
        }

        /// <summary>
        /// Starts a chunked data transfer to the specified players.
        /// Data is split into chunks and sent sequentially. Each chunk requires all target
        /// players to acknowledge receipt before the next one is sent.
        /// </summary>
        /// <param name="data">The string to transfer. Must be non-empty and within the size limit.</param>
        /// <param name="playerIds">The player IDs to send data to. Must be non-empty.</param>
        public virtual void TransferData(string data, string[] playerIds)
        {
            if (IsProcessRunning() || _pendingNextChunk)
            {
                Debug.LogWarning("[TsvrcDataSender] Transfer already in progress. Call CancelDataTransfer() first.");
                return;
            }

            // An empty player list leaves the process with no path to completion.
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
        /// Cancels the current data transfer. Safe to call at any point during a transfer,
        /// including while the process is between chunks. Has no effect if no transfer is active.
        /// </summary>
        public virtual void CancelDataTransfer()
        {
            if (_pendingNextChunk)
            {
                // The process is stopped between chunks, so cancel the pending continuation.
                ResetInternalTransferData(); // also clears _pendingNextChunk
                OnChunkSequenceStopped();
                // _pendingNextChunk is [UdonSynced] and was just cleared outside InternalCleanup,
                // so serialize explicitly. InternalCleanup is not called on this path.
                RequestSerialization();
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
        /// Only invoked on the process owner. Fires for each chunk in sequence.
        /// </summary>
        /// <param name="dataChunk">The chunk content.</param>
        /// <param name="chunkIndex">The 1-based index of this chunk.</param>
        /// <param name="totalChunks">Total number of chunks in this transfer.</param>
        /// <param name="playerIds">The player IDs that should receive this chunk.</param>
        protected virtual void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds) { }
    }
}
