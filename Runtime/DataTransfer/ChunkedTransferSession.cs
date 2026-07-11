using Tsvrc.Player;
using Tsvrc.Utils;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.DataTransfer
{
    /// <summary>
    /// Coordinates sequential delivery of chunked data to a set of players. Each chunk goes
    /// through a ready-check loop before the next one is sent. Override the virtual hooks
    /// to broadcast transfer events to all clients.
    /// </summary>
    public class ChunkedTransferSession : DataChunker
    {
        // Holds the raw data string until OnProcessStarted splits it into chunks.
        private string _initialData = "";
        // Populated only on the owner during an active transfer. Always empty on a new owner.
        private string[] _dataChunks = new string[0];
        // 1-based index of the chunk currently being transferred. Zero means no transfer is active.
        private int _currentChunkIndex = 0;
        private int _totalChunks = 0;
        // Captured before the inter-chunk gap so the player list survives PlayerTracker cleanup.
        private string[] _targetPlayerIds = new string[0];
        // True during the one-frame gap between chunks while _isRunning is temporarily false.
        // Synced so a new owner can detect the gap state and broadcast the stopped event if needed.
        [UdonSynced] private bool _pendingNextChunk = false;
        // Set when CancelDataTransfer is called after a chunk completes but before OnProcessCleanup
        // runs. Without it the cancel would be lost and the next chunk would start regardless.
        private bool _cancelRequested = false;

        protected override void OnProcessStarted()
        {
            base.OnProcessStarted();

            if (!string.IsNullOrEmpty(_initialData))
            {
                _dataChunks = CreateDataChunks(_initialData);
                _totalChunks = _dataChunks.Length;
                _initialData = "";
                _currentChunkIndex = 1;
            }

            if (_currentChunkIndex == 1)
                OnChunkSequenceStarted();

            // A subscriber may have called CancelDataTransfer inside OnChunkSequenceStarted,
            // which stops the process and resets _currentChunkIndex to 0. Guard before sending.
            if (!IsProcessRunning()) return;

            SendDataChunk(_currentChunkIndex, GetTrackedPlayerIds());
        }

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();
            // A subscriber may start a new transfer from within OnReadyCheckStopped. Guard so
            // we do not broadcast the stopped event and overwrite the new transfer's state.
            if (IsProcessRunning()) return;
            OnChunkSequenceStopped();
        }

        protected override void OnProcessCompleted()
        {
            // Capture before base fires inline callbacks, where a subscriber calling TransferData
            // would reset _currentChunkIndex and _totalChunks before we can read them.
            bool isLastChunk = _currentChunkIndex == _totalChunks;

            base.OnProcessCompleted();

            // A subscriber may have started a new transfer inside OnReadyCheckCompleted.
            if (IsProcessRunning()) return;

            if (isLastChunk)
            {
                OnChunkSequenceCompleted();
                return;
            }

            // PlayerTracker clears _trackedPlayerIds during cleanup. Capture the list now
            // so the next chunk knows which players to include.
            _targetPlayerIds = GetTrackedPlayerIds();
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            // A subscriber may have started a new transfer inside OnProcessCompleted or
            // OnProcessStopped. Do not touch chunk state if a new transfer is already running.
            if (IsProcessRunning()) return;

            if (isCompleted && _currentChunkIndex < _totalChunks && !_cancelRequested)
            {
                _currentChunkIndex++;
                // Set the gap flag before the deferred call so TransferData and CancelDataTransfer
                // behave correctly while _isRunning is false between chunks.
                _pendingNextChunk = true;
                // Defer to the next frame so the outgoing tick loop exits before a new one starts.
                SendCustomEventDelayedSeconds(nameof(_StartNextReadyCheck), 0f);
            }
            else
            {
                // Guard with isCompleted so we do not fire OnChunkSequenceStopped a second time
                // on the stop path. On the stop path it already fired inside OnProcessStopped,
                // but a subscriber calling CancelDataTransfer there can set _cancelRequested=true
                // before we arrive here. Without the guard that would cause a double fire.
                bool wasCancelled = isCompleted && _cancelRequested;
                _cancelRequested = false;
                if (wasCancelled)
                {
                    OnChunkSequenceStopped();
                    if (IsProcessRunning()) return;
                }
                ResetInternalTransferData();
            }
        }

        public void _StartNextReadyCheck()
        {
            // CancelDataTransfer may have cleared this flag during the one-frame gap.
            if (!_pendingNextChunk) return;

            _pendingNextChunk = false;

            // Player left and suspend events do not fire between chunks because they guard on
            // IsProcessRunning. Filter manually here to avoid stalling the next ready check on
            // a player who can no longer respond.
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

            // Build the filtered list in one pass, avoiding a separate count pass followed
            // by a fill pass that would call TsArray.Contains on each element twice.
            string[] filteredIds = new string[_targetPlayerIds.Length];
            int count = 0;
            for (int i = 0; i < _targetPlayerIds.Length; i++)
            {
                if (TsArray.Contains(activeNonSuspendedIds, _targetPlayerIds[i]))
                    filteredIds[count++] = _targetPlayerIds[i];
            }

            if (count == 0)
            {
                // All targets left or suspended during the gap. Broadcast stopped so receivers
                // can clean up, then serialize the cleared _pendingNextChunk.
                ResetInternalTransferData();
                OnChunkSequenceStopped();
                RequestSerialization();
                return;
            }

            if (count < _targetPlayerIds.Length)
            {
                string[] trimmed = new string[count];
                System.Array.Copy(filteredIds, trimmed, count);
                filteredIds = trimmed;
            }

            StartReadyCheck(filteredIds);
        }

        protected override void OnProcessUpdate()
        {
            base.OnProcessUpdate();

            // If all tracked players leave during an active ready check the process stalls because
            // CheckAllPlayersReady returns early on an empty list. Detect and cancel here.
            // The IsProcessRunning check avoids a redundant cancel when the base call just
            // completed the process because all players were already ready.
            if (IsProcessRunning() && GetTrackedPlayerIds().Length == 0)
                CancelDataTransfer();
        }

        protected override void OnOwnerAbandonedProcess()
        {
            // Transfer state is owner-only and unsynced, so the new owner starts with empty
            // _dataChunks and zeroed indices. Stop any active ready check to avoid a false
            // complete from CheckAllPlayersReady seeing _currentChunkIndex == _totalChunks == 0.
            //
            // If the old owner left during the inter-chunk gap (_pendingNextChunk=true, _isRunning=false),
            // the deferred _StartNextReadyCheck was lost. Broadcast stopped so receivers can clean up.
            //
            // There is a race where the old owner's InternalCleanup packet arrives after this method
            // and writes _pendingNextChunk=true. OnDeserialization handles that case.
            if (IsProcessRunning())
                StopReadyCheck();
            else if (_pendingNextChunk)
            {
                ResetInternalTransferData();
                OnChunkSequenceStopped();
                RequestSerialization();
            }
            base.OnOwnerAbandonedProcess();
        }

        public override void OnDeserialization()
        {
            base.OnDeserialization();

            // Late-packet race: the old owner's InternalCleanup packet (_pendingNextChunk=true,
            // _isRunning=false) can arrive after OnOwnerAbandonedProcess already ran and saw
            // _pendingNextChunk=false, so no stopped event was broadcast yet.
            //
            // Networking.IsOwner is used instead of IsProcessOwner because the stale packet may
            // have zeroed _ownerPlayerIdInt, making IsProcessOwner return false even though we
            // hold Unity ownership. TsvrcProcess.OnDeserialization only recovers stale ownership
            // when _isRunning=true, so this gap-state case falls through to here.
            //
            // _dataChunks.Length == 0 guards against triggering this on a new transfer that
            // started after the old one ended. A live transfer always has _dataChunks populated
            // in OnProcessStarted before any serialization goes out.
            if (Networking.IsOwner(gameObject) && !IsProcessRunning() && _pendingNextChunk
                && _dataChunks.Length == 0)
            {
                ResetInternalTransferData(); // clears _pendingNextChunk = false
                OnChunkSequenceStopped();
                RequestSerialization(); // serialize _pendingNextChunk = false to all clients
                return;
            }

            // Zombie-process race: the old owner may queue two packets when leaving mid-chunk.
            //   Packet A (InternalCleanup): _isRunning=false, _pendingNextChunk=true
            //   Packet B (StartReadyCheck): _isRunning=true,  _pendingNextChunk=false
            //
            // If both arrive after TakeOverAbandonedProcess, TsvrcProcess.OnDeserialization
            // sees packet B and restores _isRunning=true with us as owner. We now have a zombie
            // process: _isRunning=true but _dataChunks is empty because it is unsynced. The
            // next tick would false-complete and broadcast a spurious OnChunkSequenceCompleted.
            //
            // _dataChunks is always populated in OnProcessStarted before any RequestSerialization
            // fires, so it is never empty on a legitimate owner. OnDeserialization never fires
            // for the sender of RequestSerialization (VRChat guarantee), ruling out false positives.
            //
            // This stop fires twice on all clients intentionally. TsvrcProcess.OnDeserialization
            // re-broadcasts _isRunning=true via RequestSerialization, restoring _readyCheckActive=true
            // on receivers. The second stop clears it again. Duplicate transfer-stopped events are
            // suppressed further down the stack.
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

            // An empty player list would stall the process permanently with no way to complete.
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
        /// Adding tracked players while a transfer is active is not supported. Rejected with a
        /// warning instead of allowed.
        /// </summary>
        // A player added mid-transfer was never sent the chunks already broadcast before their
        // addition, so BroadcastDataChunkReceived's own playerIds check rejects every one of
        // those chunks for them and they can never call SetReady(), stalling the ready check
        // forever. _currentChunkIndex > 0 for the whole duration of a transfer, from the first
        // chunk's OnProcessStarted until ResetInternalTransferData clears it, covering both the
        // actively-in-flight and inter-chunk-gap windows. RemoveTrackedPlayers has no equivalent
        // guard: removing a tracked player mid-transfer is the normal departure path, already
        // handled by OnPlayerLeft/OnOwnerAbandonedProcess/_StartNextReadyCheck's own filtering.
        protected override bool CanAcceptTrackedPlayerAdditions()
        {
            if (_currentChunkIndex > 0)
            {
                Debug.LogWarning("[TsvrcDataSender] Cannot add tracked players while a transfer is in progress.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Cancels the current data transfer. Safe to call at any point during a transfer,
        /// including while the process is between chunks. Has no effect if no transfer is active.
        /// </summary>
        public virtual void CancelDataTransfer()
        {
            // No transfer is active. Return silently per the documented contract. Without this
            // guard, a defensive call from an OnChunkSequenceStopped subscriber would fall
            // through to StopReadyCheck and log a spurious "Process is not running" warning,
            // because all paths that fire OnChunkSequenceStopped reset state first.
            if (!IsProcessRunning() && !_pendingNextChunk && _currentChunkIndex == 0)
                return;

            if (_pendingNextChunk)
            {
                ResetInternalTransferData();
                OnChunkSequenceStopped();
                // _pendingNextChunk is synced but was cleared outside InternalCleanup,
                // so serialize explicitly.
                RequestSerialization();
                return;
            }

            // Post-completion window: the ready check completed but OnProcessCleanup has not run
            // yet. StopReadyCheck would be a no-op here, so flag OnProcessCleanup to abort instead
            // of advancing. When the last chunk already completed the transfer is done, just return.
            if (!IsProcessRunning() && _currentChunkIndex > 0)
            {
                if (_currentChunkIndex < _totalChunks)
                    _cancelRequested = true;
                return;
            }

            StopReadyCheck();
        }

        /// <summary>
        /// Resets all internal transfer state including chunk data, indices, player list, and flags.
        /// </summary>
        protected void ResetInternalTransferData()
        {
            _initialData = "";
            _dataChunks = new string[0];
            _currentChunkIndex = 0;
            _totalChunks = 0;
            _targetPlayerIds = new string[0];
            _pendingNextChunk = false;
            _cancelRequested = false;
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
