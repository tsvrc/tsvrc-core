using System;
using System.Collections.Generic;
using Tsvrc.DataTransfer;

namespace Tsvrc.Tests.Editor
{
    // One test subclass per layer of the DataTransferer chain (DataChunker : ReadyCheckProcess :
    // PlayerTracker : TsvrcProcess): lower classes get focused tests through their own type,
    // rather than always through the leaf DataTransferer, so a lower layer's tests observe only
    // that layer's own overrides, not behavior a higher layer might add on top.
    //
    // All of these carry TsvrcProcess's [UdonBehaviourSyncMode] attribute (inherited through
    // PlayerTracker), so - same reason as every other test double in this project - they live in
    // Tsvrc.Tests.Doubles, not Tests/Editor: AddComponent() silently returns null for such a
    // script defined in an Editor-platform-restricted assembly.

    /// <summary>Exposes DataChunker's protected pure-logic members for direct testing.</summary>
    public class DataChunkerTestSubclass : DataChunker
    {
        public bool InvokeValidateMessage(string message) => ValidateMessage(message);
        public string[] InvokeCreateDataChunks(string data) => CreateDataChunks(data);
        public int InvokeCalculateTotalChunks(int dataLength) => CalculateTotalChunks(dataLength);
        public string InvokeExtractChunk(string data, int chunkIndex) => ExtractChunk(data, chunkIndex);
        public static int ChunkSizeConst => CHUNK_SIZE;
        public static int MaxMessageSizeConst => MAX_MESSAGE_SIZE;
    }

    /// <summary>Records ChunkedTransferSession's own subclass hooks.</summary>
    public class ChunkedTransferSessionTestSubclass : ChunkedTransferSession
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnChunkSequenceStartedCount;
        public int OnChunkSequenceStoppedCount;
        public int OnChunkSequenceCompletedCount;

        public readonly List<int> OnDataChunkSendRequestedChunkIndexes = new List<int>();
        public readonly List<string> OnDataChunkSendRequestedDataChunks = new List<string>();
        public readonly List<int> OnDataChunkSendRequestedTotalChunks = new List<int>();
        public readonly List<string[]> OnDataChunkSendRequestedPlayerIds = new List<string[]>();

        // Lets a test inject synchronous reentrant behavior from inside a sequence hook.
        public Action OnChunkSequenceStartedAction;
        public Action OnChunkSequenceStoppedAction;
        public Action OnChunkSequenceCompletedAction;

        protected override void OnChunkSequenceStarted()
        {
            OnChunkSequenceStartedCount++;
            CallLog.Add("OnChunkSequenceStarted");
            OnChunkSequenceStartedAction?.Invoke();
        }

        protected override void OnChunkSequenceStopped()
        {
            OnChunkSequenceStoppedCount++;
            CallLog.Add("OnChunkSequenceStopped");
            OnChunkSequenceStoppedAction?.Invoke();
        }

        protected override void OnChunkSequenceCompleted()
        {
            OnChunkSequenceCompletedCount++;
            CallLog.Add("OnChunkSequenceCompleted");
            OnChunkSequenceCompletedAction?.Invoke();
        }

        protected override void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            CallLog.Add("OnDataChunkSendRequested");
            OnDataChunkSendRequestedDataChunks.Add(dataChunk);
            OnDataChunkSendRequestedChunkIndexes.Add(chunkIndex);
            OnDataChunkSendRequestedTotalChunks.Add(totalChunks);
            OnDataChunkSendRequestedPlayerIds.Add(playerIds);
        }

        // TsSubscribe target for reaching the narrow "post-chunk-completion, pre-cleanup" cancel
        // window on a non-last chunk: this event fires from deep inside base.OnProcessCompleted(),
        // strictly before ChunkedTransferSession's own OnProcessCompleted() continuation
        // (isLastChunk check, _targetPlayerIds capture) resumes.
        public void _CancelDuringReadyCheckCompletedEvent() => CancelDataTransfer();

        // Same reentrancy window as above, but for a full restart instead of a cancel: a
        // subscriber to ReadyCheckProcess's own OnReadyCheckCompletedEvent (which fires for
        // EVERY chunk, not just the last one) calling TransferData() again.
        // One-shot by design: TsSubscribe subscriptions are permanent ("world-lifetime, never
        // cleared" per its own doc comment), so without clearing RestartPlayerIds after firing,
        // this handler would also fire when the RESTARTED transfer's own chunk completes,
        // triggering an unbounded chain of further restarts instead of ever letting a transfer
        // reach OnChunkSequenceCompleted.
        public string[] RestartPlayerIds;
        public void _RestartDuringReadyCheckCompletedEvent()
        {
            if (RestartPlayerIds == null) return;
            string[] playerIds = RestartPlayerIds;
            RestartPlayerIds = null;
            TransferData("restarted", playerIds);
        }
    }

    /// <summary>Records DataSender's transfer-lifecycle hooks and network-callable events.</summary>
    public class DataSenderTestSubclass : DataSender
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnTransferStartedCount;
        public int OnTransferStoppedCount;
        public int OnTransferCompletedCount;

        public readonly List<string> ReceivedEvents = new List<string>();

        public Action OnTransferStartedAction;
        public Action OnTransferStoppedAction;
        public Action OnTransferCompletedAction;

        protected override void OnTransferStarted()
        {
            OnTransferStartedCount++;
            CallLog.Add("OnTransferStarted");
            OnTransferStartedAction?.Invoke();
        }

        protected override void OnTransferStopped()
        {
            OnTransferStoppedCount++;
            CallLog.Add("OnTransferStopped");
            OnTransferStoppedAction?.Invoke();
        }

        protected override void OnTransferCompleted()
        {
            OnTransferCompletedCount++;
            CallLog.Add("OnTransferCompleted");
            OnTransferCompletedAction?.Invoke();
        }

        public void _OnDataTransferStartedEventReceived() => ReceivedEvents.Add("OnDataTransferStartedEvent");
        public void _OnDataTransferStoppedEventReceived() => ReceivedEvents.Add("OnDataTransferStoppedEvent");
        public void _OnDataTransferCompletedEventReceived() => ReceivedEvents.Add("OnDataTransferCompletedEvent");
    }

    /// <summary>Records DataChunkReceiver's chunk-assembly hooks.</summary>
    public class DataChunkReceiverTestSubclass : DataChunkReceiver
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnChunksAssembledCount;
        public string LastAssembledDataSeen;

        public int OnChunkStoredCount;
        public readonly List<int> OnChunkStoredChunkIndexes = new List<int>();
        public readonly List<int> OnChunkStoredTotalChunks = new List<int>();

        public readonly List<int> OnDataChunkSendRequestedChunkIndexes = new List<int>();

        public string[] PeekReceivedChunks() => _receivedChunks;

        // Stubs out the real self-dispatch (DataChunkReceiver's own override, which would
        // otherwise call BroadcastDataChunkReceived automatically via SendCustomNetworkEvent
        // every time SendDataChunk runs), isolating this layer to BroadcastDataChunkReceived's
        // own validation logic under full manual control - mirroring how
        // ChunkedTransferSessionTestSubclass isolates itself from DataSender/DataChunkReceiver.
        protected override void OnDataChunkSendRequested(string dataChunk, int chunkIndex, int totalChunks, string[] playerIds)
        {
            OnDataChunkSendRequestedChunkIndexes.Add(chunkIndex);
            CallLog.Add("OnDataChunkSendRequested");
        }

        protected override void OnChunksAssembled(string assembledData)
        {
            OnChunksAssembledCount++;
            LastAssembledDataSeen = assembledData;
            CallLog.Add("OnChunksAssembled");
        }

        protected override void OnChunkStored(int chunkIndex, int totalChunks)
        {
            OnChunkStoredCount++;
            OnChunkStoredChunkIndexes.Add(chunkIndex);
            OnChunkStoredTotalChunks.Add(totalChunks);
            CallLog.Add("OnChunkStored");
        }
    }

    /// <summary>Records DataSenderReceiver's all-clients events, on top of its own Last* properties.</summary>
    public class DataSenderReceiverTestSubclass : DataSenderReceiver
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnDataReceptionStartedCount;
        public int OnDataReceptionStoppedCount;
        public int OnDataReceptionCompletedCount;
        public int OnDataChunkReceivedCount;

        public readonly List<string> ReceivedEvents = new List<string>();

        protected override void OnDataReceptionStarted()
        {
            OnDataReceptionStartedCount++;
            CallLog.Add("OnDataReceptionStarted");
        }

        protected override void OnDataReceptionStopped()
        {
            OnDataReceptionStoppedCount++;
            CallLog.Add("OnDataReceptionStopped");
        }

        protected override void OnDataReceptionCompleted()
        {
            OnDataReceptionCompletedCount++;
            CallLog.Add("OnDataReceptionCompleted");
        }

        protected override void OnDataChunkReceived()
        {
            OnDataChunkReceivedCount++;
            CallLog.Add("OnDataChunkReceived");
        }

        public void _OnDataReceptionStartedEventReceived()
        {
            ReceivedEvents.Add("OnDataReceptionStartedEvent");
            CallLog.Add("OnDataReceptionStartedEvent");
        }

        public void _OnDataReceptionStoppedEventReceived()
        {
            ReceivedEvents.Add("OnDataReceptionStoppedEvent");
            CallLog.Add("OnDataReceptionStoppedEvent");
        }

        public void _OnDataReceptionCompletedEventReceived()
        {
            ReceivedEvents.Add("OnDataReceptionCompletedEvent");
            CallLog.Add("OnDataReceptionCompletedEvent");
        }

        public void _OnDataChunkReceivedEventReceived()
        {
            ReceivedEvents.Add("OnDataChunkReceivedEvent");
            CallLog.Add("OnDataChunkReceivedEvent");
        }
    }

    /// <summary>
    /// Full black-box double for end-to-end DataTransferer integration tests. Records every
    /// OnTransfer*Event via TsSubscribe/TsEmit (the only way to observe DataTransferer's own,
    /// non-overridable public event surface) rather than overriding hooks.
    /// </summary>
    public class DataTransfererTestSubclass : DataTransferer
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnTransferStartedEventCount;
        public int OnTransferStoppedEventCount;
        public int OnTransferCompletedEventCount;
        public int OnTransferChunkEventCount;

        public void _OnTransferStarted()
        {
            OnTransferStartedEventCount++;
            CallLog.Add("OnTransferStartedEvent");
        }

        public void _OnTransferStopped()
        {
            OnTransferStoppedEventCount++;
            CallLog.Add("OnTransferStoppedEvent");
        }

        public void _OnTransferCompleted()
        {
            OnTransferCompletedEventCount++;
            CallLog.Add("OnTransferCompletedEvent");
        }

        public void _OnTransferChunk()
        {
            OnTransferChunkEventCount++;
            CallLog.Add("OnTransferChunkEvent");
        }

        // Subscribes this instance to all four of its own events, in one call, for tests that
        // don't need per-test control over which events are wired up.
        public void SubscribeToAllTransferEvents()
        {
            TsSubscribe(this, OnTransferStartedEvent, nameof(_OnTransferStarted));
            TsSubscribe(this, OnTransferStoppedEvent, nameof(_OnTransferStopped));
            TsSubscribe(this, OnTransferCompletedEvent, nameof(_OnTransferCompleted));
            TsSubscribe(this, OnTransferChunkEvent, nameof(_OnTransferChunk));
        }
    }
}
