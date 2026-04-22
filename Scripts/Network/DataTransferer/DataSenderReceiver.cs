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

        protected string[] _receivedChunks = new string[0];

        public string LastData { get; private set; } = "";
        public int LastChunkIndex { get; private set; } = 0;
        public int LastTotalChunks { get; private set; } = 0;

        public override void TsRelease()
        {
            // DataSender.TsRelease calls ResetInternalTransferData() which only clears DataSender
            // fields. TsvrcBehaviour.ResetBehaviourState() only clears subscriptions.
            // Neither touches the receiver-side fields below, leaving stale data visible to
            // callers after pool reuse (before the next transfer's _OnDataTransferStarted fires).
            ResetReceiverState();
            base.TsRelease();
        }

        protected override void TsStart()
        {
            base.TsStart();
            TsSubscribe(this, OnDataTransferStartedEvent, nameof(_OnDataTransferStarted));
            TsSubscribe(this, OnDataTransferStoppedEvent, nameof(_OnDataTransferStopped));
            TsSubscribe(this, OnDataTransferCompletedEvent, nameof(_OnDataTransferCompleted));
        }

        #region DataSender Callbacks

        public void _OnDataTransferStarted()
        {
            ResetReceiverState();   // clears _transferActive, chunks, and Last* properties
            _transferActive = true; // set true after reset: chunks for this transfer are now accepted
            TsEmit(OnDataReceptionStartedEvent);
        }

        public void _OnDataTransferStopped()
        {
            ResetReceiverState(); // clears _transferActive = false
            TsEmit(OnDataReceptionStoppedEvent);
        }

        public void _OnDataTransferCompleted()
        {
            // Assemble before clearing _receivedChunks; clear _transferActive before emitting
            // so any code running in the user callback sees the correct idle state.
            LastData = ReassembleMessage(_receivedChunks);
            _receivedChunks = new string[0];
            _transferActive = false;

            TsEmit(OnDataReceptionCompletedEvent);
        }

        protected override void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            base.OnDataChunkSendRequested(dataChunk, chunkIndex, totalChunks, playerIds);

            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(BroadcastDataChunkReceived), dataChunk, chunkIndex, totalChunks, playerIds);
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Resets receiver-side state. Called on start, stop, and TsRelease.
        /// </summary>
        protected void ResetReceiverState()
        {
            _transferActive = false;
            _receivedChunks = new string[0];
            LastData = "";
            LastChunkIndex = 0;
            LastTotalChunks = 0;
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

            NotifyChunkReceived();

            TsEmit(OnDataChunkReceivedEvent);
        }

        #endregion
    }
}
