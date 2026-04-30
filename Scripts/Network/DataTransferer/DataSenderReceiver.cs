using Tsvrc.Player;
using Tsvrc.Utils;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Network
{
    public class DataSenderReceiver : DataSender
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

        // Ceiling division of MAX_MESSAGE_SIZE / CHUNK_SIZE. A [NetworkCallable] call with
        // totalChunks > _maxChunks is impossible from legitimate code and must be rejected to
        // prevent a malicious player from triggering new string[Int32.MaxValue] → OOM on all clients.
        private const int _maxChunks = (MAX_MESSAGE_SIZE + CHUNK_SIZE - 1) / CHUNK_SIZE;

        // playerId (int) of the player who initiated the current transfer, captured in OnTransferStarted()
        // via Networking.LocalPlayer.playerId (owner) or CallingPlayer.playerId (remote).
        // Validated in BroadcastDataChunkReceived to reject chunks injected by other players.
        // int comparison avoids the string allocations that TsPlayer.GetPlayerID would require.
        // Reset to 0 by ResetReceiverState(); valid playerIds are >=1 so 0 is a safe sentinel.
        private int _expectedSenderPlayerId = 0;

        // totalChunks value from the first accepted chunk of the current transfer.
        // Subsequent chunks must carry the same value; a mismatch means either a retransmit
        // from a new conflicting transfer or a malicious injection, and both are rejected.
        // Reset to 0 by ResetReceiverState() so the first chunk of a new transfer can set it.
        private int _expectedTotalChunks = 0;

        // Set immediately before and cleared immediately after SendCustomNetworkEvent(All, ...) in
        // OnDataChunkSendRequested. When true, BroadcastDataChunkReceived skips the CallingPlayer
        // validation because CallingPlayer is unreliable during the owner's own inline call:
        // may be propagated from an outer network event context (VRChat docs: "InNetworkCall is
        // only reset once the entry function terminates"). Reliable because Udon is single-threaded:
        // the flag cannot be true when a network event from another player arrives.
        private bool _isSendingChunk = false;

        // Used instead of IsProcessRunning() in BroadcastDataChunkReceived to avoid a race:
        // [UdonSynced] _isRunning arrives via manual-sync serialization, which VRChat does not
        // order relative to network events. A chunk event can arrive before _isRunning=true and
        // be silently dropped, stalling the transfer. Network events from the same sender are
        // ordered (VRChat docs), and NotifyTrackedPlayersDataTransferStarted is always sent before
        // the first BroadcastDataChunkReceived, so _transferActive is guaranteed set first.
        private bool _transferActive = false;

        // Staging fields for the deferred completion emit (see OnTransferCompleted).
        // _pendingCompletionData holds the assembled string until the deferred emit fires, so
        // a new TransferData() call (which resets LastData = "" via ResetReceiverState())
        // cannot corrupt the pending data. _pendingCompletion guards against a spurious emit
        // if a new transfer starts before the deferred call fires: ResetReceiverState() clears
        // it to false, causing _EmitDataReceptionCompleted to return early.
        private bool _pendingCompletion = false;
        private string _pendingCompletionData = "";

        // Mirror of _pendingCompletion for the stopped path. OnTransferStopped defers the emit
        // so InternalCleanup finishes before user callbacks run. A TransferData() call from the
        // callback would otherwise fire inside ExecuteStop and corrupt the new transfer's state.
        // ResetReceiverState() clears this to suppress a stale emit if a new transfer starts
        // before the deferred call fires.
        private bool _pendingStop = false;

        protected string[] _receivedChunks = new string[0];

        public string LastData { get; private set; } = "";
        public int LastChunkIndex { get; private set; } = 0;
        public int LastTotalChunks { get; private set; } = 0;

        protected override void OnTransferStarted()
        {
            ResetReceiverState();
            _transferActive = true; // set after reset so chunks for this transfer are accepted

            // Capture the sender ID for BroadcastDataChunkReceived's caller validation.
            // CallingPlayer cannot be used here: it propagates through the entire call stack of
            // any network event handler (VRChat docs: "InNetworkCall is only reset once the entry
            // function terminates"). If the owner calls TransferData() from inside a network event
            // callback (e.g. OnReadyCheckCompleted, which fires inline from BroadcastAddReadyPlayer),
            // CallingPlayer = the remote sender of that outer event, which would cause remote
            // clients to reject the owner's chunks, stalling the transfer indefinitely.
            // IsProcessOwner() is safe here: TsvrcProcess.StartProcess() sets _ownerId
            // synchronously before OnProcessStarted() fires, so it is always accurate at this point.
            if (IsProcessOwner())
            {
                _expectedSenderPlayerId = Networking.LocalPlayer.playerId;
            }
            else
            {
                // CallingPlayer is always non-null for legitimate remote reception of a
                // [NetworkCallable] method. The _localPlayerId fallback covers the unreachable
                // edge case of a direct local call on a non-owner (user error).
                var cp = NetworkCalling.CallingPlayer;
                _expectedSenderPlayerId = cp != null ? cp.playerId : Networking.LocalPlayer.playerId;
            }

            OnDataReceptionStarted();
            TsEmit(OnDataReceptionStartedEvent);
        }

        protected override void OnTransferStopped()
        {
            ResetReceiverState();
            _pendingStop = true;
            // Defer so InternalCleanup finishes before user callbacks run (same reason as OnTransferCompleted).
            SendCustomEventDelayedSeconds(nameof(_EmitDataReceptionStopped), 0f);
        }

        protected override void OnTransferCompleted()
        {
            // Stage assembled data: LastData is assigned in _EmitDataReceptionCompleted atomically
            // with the emission. If a new transfer starts before the deferred emit fires,
            // ResetReceiverState() clears _pendingCompletion, causing the early return before
            // _pendingCompletionData is read (ResetReceiverState also clears that field).
            _pendingCompletionData = ReassembleMessage(_receivedChunks);
            _receivedChunks = new string[0];
            _transferActive = false;
            _pendingCompletion = true;

            // Defer so InternalCleanup finishes before user callbacks run. Without deferral,
            // TransferData() from the completion callback fires inside ExecuteComplete before
            // InternalCleanup, corrupting the new transfer's process state.
            SendCustomEventDelayedSeconds(nameof(_EmitDataReceptionCompleted), 0f);
        }

        // Underscore prefix: local-only, cannot be triggered via network event.
        // Called by SendCustomEventDelayedSeconds in OnTransferStopped.
        public void _EmitDataReceptionStopped()
        {
            // Guard: ResetReceiverState (called by OnTransferStarted or a new
            // TransferData start) clears _pendingStop = false. Without this, a new transfer
            // starting before this fires would deliver a spurious OnDataReceptionStoppedEvent.
            if (!_pendingStop) return;
            _pendingStop = false;
            OnDataReceptionStopped();
            TsEmit(OnDataReceptionStoppedEvent);
        }

        // Underscore prefix: local-only, cannot be triggered via network event.
        // Called by SendCustomEventDelayedSeconds in OnTransferCompleted.
        public void _EmitDataReceptionCompleted()
        {
            // Guard: ResetReceiverState (called from OnTransferStarted on a new TransferData start)
            // clears _pendingCompletion = false. Without this check, a new transfer starting between
            // the deferral and this firing would deliver a spurious OnDataReceptionCompletedEvent
            // with empty LastData to the new subscribers.
            if (!_pendingCompletion) return;
            _pendingCompletion = false;

            // Reachable only when _pendingCompletion was not cleared by ResetReceiverState,
            // so _pendingCompletionData is guaranteed to contain valid assembled data.
            LastData = _pendingCompletionData;
            _pendingCompletionData = "";

            OnDataReceptionCompleted();
            TsEmit(OnDataReceptionCompletedEvent);
        }

        protected override void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            base.OnDataChunkSendRequested(dataChunk, chunkIndex, totalChunks, playerIds);

            // Flag our own inline execution so BroadcastDataChunkReceived can skip the
            // CallingPlayer check, which would be wrong here due to possible propagation.
            // Cleared immediately after the synchronous call returns.
            _isSendingChunk = true;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastDataChunkReceived), dataChunk, chunkIndex, totalChunks, playerIds);
            _isSendingChunk = false;
        }

        /// <summary>Called on all clients when data reception starts.</summary>
        protected virtual void OnDataReceptionStarted() { }
        /// <summary>Called on all clients when data reception is stopped before completion.</summary>
        protected virtual void OnDataReceptionStopped() { }
        /// <summary>Called on all clients when data reception completes. <c>LastData</c> is set before this fires.</summary>
        protected virtual void OnDataReceptionCompleted() { }
        /// <summary>Called on all tracked clients when a data chunk is received. <c>LastChunkIndex</c> and <c>LastTotalChunks</c> are set before this fires.</summary>
        protected virtual void OnDataChunkReceived() { }

        /// <summary>
        /// Resets receiver-side state. Called on transfer start and stop.
        /// </summary>
        protected void ResetReceiverState()
        {
            _transferActive = false;
            _receivedChunks = new string[0];
            _expectedSenderPlayerId = 0;
            _expectedTotalChunks = 0;
            LastData = "";
            LastChunkIndex = 0;
            LastTotalChunks = 0;
            // Clear staging fields so the deferred _EmitDataReceptionCompleted/_EmitDataReceptionStopped
            // from a previous transfer see their flags as false and return early, preventing spurious
            // events with stale data after a new transfer start.
            _pendingCompletion = false;
            _pendingCompletionData = "";
            _pendingStop = false;
        }

        /// <summary>
        /// Reassembles the complete message from received chunks.
        /// </summary>
        protected string ReassembleMessage(string[] receivedChunks)
        {
            // Pre-allocate to the exact total length to avoid repeated internal buffer
            // reallocations. The default StringBuilder capacity is 16 chars; without
            // pre-allocation a 500,000-char message (200 chunks × 2,500 chars) would
            // resize the buffer ~15 times, each doubling it and copying all prior data.
            // Chunks are guaranteed non-null at this call site: ReassembleMessage is only
            // called from OnTransferCompleted, which is only reached after all N chunks have
            // been accepted (each ready check completes only when all tracked players ACK
            // the current chunk, and chunks arrive in strict order due to the sequential
            // ready-check protocol).
            int totalLength = 0;
            for (int i = 0; i < receivedChunks.Length; i++)
                totalLength += receivedChunks[i].Length;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(totalLength);
            for (int i = 0; i < receivedChunks.Length; i++)
                sb.Append(receivedChunks[i]);
            return sb.ToString();
        }

        /// <summary>
        /// Notifies the sender that this chunk was received by marking ready.
        /// </summary>
        protected void NotifyChunkReceived()
        {
            SetReady();
        }

        /// <summary>
        /// Network callable method to broadcast the reception of a data chunk.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        // maxEventsPerSecond: VRChat splits events >1 KB into internal 1-KB packets, each counted
        // against the rate budget (VRChat docs: 'these internal events are visible in the rate-limiting
        // queue'). CHUNK_SIZE=2500-char ASCII chunk ≈ 2,500 bytes → ~3 internal events; a CJK chunk
        // ≈ 7,500 bytes UTF-8 → ~8 internal events. At the default 5/s budget, even an ASCII
        // chunk would stall ~0.6 s; a CJK chunk ~1.6 s. 100/s delivers ASCII chunks in ~0.03 s and
        // CJK chunks in ~0.08 s, well within the global ~11 KB/s throughput cap (network-details
        // page), which is the real upper bound on transfer speed.
        [NetworkCallable(maxEventsPerSecond: 100)]
        public void BroadcastDataChunkReceived(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            // Use _transferActive (local, event-driven) rather than IsProcessRunning() (synced field).
            // See field declaration above for the full explanation of the race condition this avoids.
            if (!_transferActive) return;

            // Reject chunks not sent by the process owner. Any player can call this [NetworkCallable]
            // to inject arbitrary data or trigger false SetReady() ACKs without this check.
            // _isSendingChunk is true only during the owner's own synchronous SendCustomNetworkEvent
            // call (see OnDataChunkSendRequested), bypassing the CallingPlayer check, which is
            // unreliable there due to possible propagation from an outer network event context.
            // For all network-delivered calls, CallingPlayer is the actual packet sender:
            //   Owner's chunk:     CallingPlayer = owner → playerId matches _expectedSenderPlayerId.
            //   Malicious chunk:   CallingPlayer = attacker → playerId mismatch → rejected.
            //   Direct local call: CallingPlayer = null → rejected.
            if (!_isSendingChunk)
            {
                var caller = NetworkCalling.CallingPlayer;
                if (caller == null || caller.playerId != _expectedSenderPlayerId) return;
            }

            // Any [NetworkCallable] parameter can be null from a malicious call;
            // TsArray.Contains crashes on null.Length without this check.
            if (playerIds == null) return;

            // _localPlayerId is cached once in TsvrcProcess.TsStart(); VRChat guarantees
            // displayName and playerId are immutable for the duration of a session.
            var playerId = _localPlayerId;
            if (!TsArray.Contains(playerIds, playerId)) return;

            // null dataChunk is stored silently (StringBuilder.Append(null) is a no-op),
            // leaving that slot empty and corrupting the reassembled message.
            if (dataChunk == null) return;

            // Unbounded totalChunks → OOM on new string[totalChunks];
            // _maxChunks is the ceiling of MAX_MESSAGE_SIZE / CHUNK_SIZE.
            if (totalChunks < 1 || totalChunks > _maxChunks) return;

            if (chunkIndex < 1 || chunkIndex > totalChunks) return;

            // Lock in totalChunks from the first chunk. A mismatch on later chunks means a
            // malicious call; without this guard a different value would silently reallocate
            // _receivedChunks and discard all previously assembled data.
            if (_expectedTotalChunks == 0)
            {
                _expectedTotalChunks = totalChunks; // first chunk: lock in the expected count
            }
            else if (totalChunks != _expectedTotalChunks)
            {
                return;
            }

            if (_receivedChunks.Length != totalChunks)
            {
                _receivedChunks = new string[totalChunks];
            }

            // Suppress duplicate deliveries: VRChat docs describe internal event splitting as
            // 'almost transparent' — the 'almost' covers edge cases (e.g. network anomalies,
            // ownership-transfer races) where the same logical event could be delivered twice.
            // Without this guard, a duplicate would overwrite the slot, re-fire OnDataChunkReceived,
            // and send a redundant SetReady() ACK to the owner.
            if (_receivedChunks[chunkIndex - 1] != null) return;

            _receivedChunks[chunkIndex - 1] = dataChunk;
            LastChunkIndex = chunkIndex;
            LastTotalChunks = totalChunks;

            // Emit before NotifyChunkReceived(): on the owner, NotifyChunkReceived() can
            // synchronously trigger completion (SetReady → CheckAllPlayersReady → ExecuteComplete).
            // Emitting first guarantees chunk events always precede completion events.
            OnDataChunkReceived();
            TsEmit(OnDataChunkReceivedEvent);

            NotifyChunkReceived();
        }
    }
}
