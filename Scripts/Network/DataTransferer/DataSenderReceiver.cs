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

        // Network-event-driven flag: true while a data transfer is in progress.
        // Used in BroadcastDataChunkReceived instead of IsProcessRunning() to avoid a race
        // condition: the [UdonSynced] _isRunning field is delivered via manual-sync serialization
        // which VRChat does NOT order relative to network events sent in the same frame. A chunk
        // event can therefore arrive before _isRunning=true, causing IsProcessRunning() to return
        // false and the chunk to be silently dropped — the player never calls SetReady() and the
        // transfer stalls indefinitely.
        // Network events from the same sender ARE ordered (creators.vrchat.com/worlds/udon/
        // networking/events: "Events from the same Udon source are received in the order they
        // were sent"). NotifyTrackedPlayersDataTransferStarted is always sent before the first
        // BroadcastDataChunkReceived in OnProcessStarted, so _transferActive is always true by
        // the time any chunk event arrives on any client.
        private bool _transferActive = false;

        // Staging fields for the deferred completion emit.
        // OnTransferCompleted sets _transferActive=false and defers the emission by one frame
        // so that InternalCleanup finishes before user callbacks run (see comment on that method).
        // In the same Udon frame, user code (or another network event) can call TransferData(),
        // which fires StartReadyCheck → NotifyTrackedPlayersDataTransferStarted inline on the
        // sender → OnTransferStarted → ResetReceiverState(), clearing LastData = "".
        // When the deferred _EmitDataReceptionCompleted fires next frame the callback would then
        // read empty LastData. Staging in _pendingCompletionData preserves the assembled string
        // across the ResetReceiverState call and only assigns LastData at emit time.
        // _pendingCompletion prevents a spurious emit when a new TransferData start
        // happens between the deferral and the firing: ResetReceiverState sets it to false,
        // so _EmitDataReceptionCompleted sees no pending emit and returns early.
        private bool _pendingCompletion = false;
        private string _pendingCompletionData = "";

        // Mirror of _pendingCompletion for the stopped path.
        // OnTransferStopped defers TsEmit(OnDataReceptionStoppedEvent) by one frame so that
        // InternalCleanup finishes before user callbacks run. Without the deferral, user code
        // calling TransferData() from the stopped callback fires inside ExecuteStop before
        // InternalCleanup has run, which then overwrites the new transfer's _ownerId,
        // _trackedPlayerIds, _useProcessUpdate, _updateLoopActive, and _dataChunks — silently
        // killing it. ResetReceiverState clears this flag, suppressing a stale stopped emit
        // if a new TransferData start runs before the deferred call fires.
        private bool _pendingStop = false;

        protected string[] _receivedChunks = new string[0];

        public string LastData { get; private set; } = "";
        public int LastChunkIndex { get; private set; } = 0;
        public int LastTotalChunks { get; private set; } = 0;

        #region DataSender Overrides

        protected override void OnTransferStarted()
        {
            ResetReceiverState();   // clears _transferActive, chunks, and Last* properties
            _transferActive = true; // set true after reset: chunks for this transfer are now accepted
            OnDataReceptionStarted();
            TsEmit(OnDataReceptionStartedEvent);
        }

        protected override void OnTransferStopped()
        {
            ResetReceiverState(); // clears _transferActive, _pendingCompletion, _pendingStop, etc.
            _pendingStop = true;
            // Defer for the same reason as OnTransferCompleted: TsEmit is synchronous and
            // fires while ExecuteStop is still on the call stack (InternalCleanup not yet run).
            // A TransferData() call from the user callback would have its state wiped by the
            // subsequent InternalCleanup. Source: creators.vrchat.com/worlds/udon/networking/
            // events — "trigger locally before moving on, just like a regular function call would".
            SendCustomEventDelayedSeconds(nameof(_EmitDataReceptionStopped), 0f);
        }

        protected override void OnTransferCompleted()
        {
            // Stage into _pendingCompletionData rather than LastData directly. If user code or
            // another network event calls TransferData() in the same Udon frame, the resulting
            // inline OnTransferStarted → ResetReceiverState() clears LastData = "" but
            // leaves _pendingCompletionData intact. LastData is only assigned in
            // _EmitDataReceptionCompleted, atomically with the emission.
            _pendingCompletionData = ReassembleMessage(_receivedChunks);
            _receivedChunks = new string[0];
            _transferActive = false;
            _pendingCompletion = true;

            // Defer emission to the next frame. TsEmit is synchronous — if user code in
            // OnDataReceptionCompletedEvent calls TransferData(), it fires inside ExecuteComplete
            // before InternalCleanup has run, corrupting the new transfer's _ownerId,
            // _trackedPlayerIds, _useProcessUpdate, and _updateLoopActive.
            // SendCustomEventDelayedSeconds is local-only (never sent over network) and defers
            // to the next frame, ensuring InternalCleanup finishes before user callbacks run.
            // Source: creators.vrchat.com/worlds/udon/networking/events —
            // "trigger locally before moving on, just like a regular function call would".
            SendCustomEventDelayedSeconds(nameof(_EmitDataReceptionCompleted), 0f);
        }

        #endregion

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

            // Assign LastData atomically with the emission. This is safe even if ResetReceiverState
            // ran between OnTransferCompleted and here — _pendingCompletionData is preserved
            // through that call (see staging field comment above), unlike LastData which gets "".
            LastData = _pendingCompletionData;
            _pendingCompletionData = "";

            OnDataReceptionCompleted();
            TsEmit(OnDataReceptionCompletedEvent);
        }

        protected override void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            base.OnDataChunkSendRequested(dataChunk, chunkIndex, totalChunks, playerIds);

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastDataChunkReceived), dataChunk, chunkIndex, totalChunks, playerIds);
        }

        #region Reception Virtual Methods

        /// <summary>Called on all clients when data reception starts.</summary>
        protected virtual void OnDataReceptionStarted() { }
        /// <summary>Called on all clients when data reception is stopped before completion.</summary>
        protected virtual void OnDataReceptionStopped() { }
        /// <summary>Called on all clients when data reception completes. <c>LastData</c> is set before this fires.</summary>
        protected virtual void OnDataReceptionCompleted() { }
        /// <summary>Called on all tracked clients when a data chunk is received. <c>LastChunkIndex</c> and <c>LastTotalChunks</c> are set before this fires.</summary>
        protected virtual void OnDataChunkReceived() { }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Resets receiver-side state. Called on transfer start and stop.
        /// </summary>
        protected void ResetReceiverState()
        {
            _transferActive = false;
            _receivedChunks = new string[0];
            LastData = "";
            LastChunkIndex = 0;
            LastTotalChunks = 0;
            // Clear staging fields so the deferred _EmitDataReceptionCompleted/_EmitDataReceptionStopped
            // from a previous transfer see their flags as false and return early — prevents spurious
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
            // SetReady() now uses the local _readyCheckActive flag instead of IsProcessRunning(),
            // and no longer guards on IsPlayerReady() — both races are fixed in ReadyCheckProcess.
            SetReady();
        }

        #endregion

        #region Network Events

        /// <summary>
        /// Network callable method to broadcast the reception of a data chunk.
        /// This method is invoked on all players via network event, but only executes for tracked players.
        /// </summary>
        [NetworkCallable]
        public void BroadcastDataChunkReceived(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            // Use _transferActive (local, event-driven) rather than IsProcessRunning() (synced field).
            // See field declaration above for the full explanation of the race condition this avoids.
            if (!_transferActive) return;

            // Null guard: VRChat delivers null for nullable parameters sent as null
            // (confirmed at creators.vrchat.com/worlds/udon/networking/events).
            // TsArray.Contains accesses array.Length without a null check — null crashes.
            if (playerIds == null) return;

            var playerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            if (!TsArray.Contains(playerIds, playerId)) return;

            // Null guard: a malicious [NetworkCallable] call with null dataChunk would store
            // null into _receivedChunks. StringBuilder.Append(null) is a no-op (.NET spec),
            // so no crash, but that chunk is silently missing from the assembled message.
            if (dataChunk == null) return;

            // Validate totalChunks before using it to allocate an array. An unchecked large value
            // (e.g. Int32.MaxValue) passes the chunkIndex > totalChunks guard when chunkIndex = 1
            // and causes an OutOfMemoryException on the new string[totalChunks] line below.
            if (totalChunks < 1 || totalChunks > _maxChunks) return;

            if (chunkIndex < 1 || chunkIndex > totalChunks) return;

            if (_receivedChunks.Length != totalChunks)
            {
                _receivedChunks = new string[totalChunks];
            }

            _receivedChunks[chunkIndex - 1] = dataChunk;
            LastChunkIndex = chunkIndex;
            LastTotalChunks = totalChunks;

            // Emit the chunk event BEFORE calling NotifyChunkReceived(). When the sender is also
            // a receiver (owner in playerIds) and is the last player to call SetReady(), the
            // NotifyChunkReceived() → SetReady() → BroadcastAddReadyPlayer inline chain triggers
            // CheckAllPlayersReady() → ExecuteComplete() → fires the completion events — all
            // synchronously (VRChat docs: sender executes inline "like a regular function call").
            // Emitting here first guarantees chunk events always precede completion events,
            // preserving correct ordering for progress-reporting subscribers.
            OnDataChunkReceived();
            TsEmit(OnDataChunkReceivedEvent);

            NotifyChunkReceived();
        }

        #endregion
    }
}
