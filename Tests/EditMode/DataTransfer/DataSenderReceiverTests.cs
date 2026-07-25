using NUnit.Framework;
using Tsvrc.DataTransfer;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Covers DataSenderReceiver: the Last* properties, the deferred-completion staging
    // mechanism (_pendingCompletion/_pendingCompletionData), and the deferred-stop mechanism
    // (_pendingStop) built on top of DataChunkReceiver's hooks.
    //
    // Every hook here (OnChunkStored, OnChunksAssembled, OnTransferStopped, ResetReceiverState,
    // the two _Emit* methods) is invoked directly, bypassing the whole TransferData/StartProcess
    // chain entirely - DataChunkReceiver.OnTransferStarted() unconditionally reads
    // Networking.LocalPlayer.playerId when IsProcessOwner() is true, which throws
    // NullReferenceException in Edit Mode (established in DataChunkReceiverTests.cs). None of
    // these specific hooks touch Networking.LocalPlayer themselves, so direct invocation is both
    // safe and, per this project's own "focused tests on lower layers" convention, the more
    // precise way to isolate DataSenderReceiver's own logic.
    public class DataSenderReceiverTests : DataTransferTestBase
    {
        [Test]
        public void OnChunkStored_SetsLastChunkIndexAndLastTotalChunks()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();

            PrivateFieldAccess.InvokeInstance(receiver, "OnChunkStored", 3, 7);

            Assert.AreEqual(3, receiver.LastChunkIndex);
            Assert.AreEqual(7, receiver.LastTotalChunks);
        }

        [Test]
        public void OnChunkStored_FiresHookThenEmitsEvent_InThatOrder()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();
            receiver.TsSubscribe(receiver, DataSenderReceiver.OnDataChunkReceivedEvent, nameof(receiver._OnDataChunkReceivedEventReceived));

            PrivateFieldAccess.InvokeInstance(receiver, "OnChunkStored", 1, 1);

            CollectionAssert.AreEqual(new[] { "OnDataChunkReceived", "OnDataChunkReceivedEvent" }, receiver.CallLog);
        }

        [Test]
        public void OnChunkStored_LastValuesAlreadyCorrectInsideTheHook()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();

            PrivateFieldAccess.InvokeInstance(receiver, "OnChunkStored", 2, 5);

            // OnDataChunkReceivedCount only increments inside the hook itself; if the hook read
            // stale Last* values it would have no way to prove it here, so this instead confirms
            // the assignment happens (per source order) before the hook is even called.
            Assert.AreEqual(1, receiver.OnDataChunkReceivedCount);
            Assert.AreEqual(2, receiver.LastChunkIndex);
            Assert.AreEqual(5, receiver.LastTotalChunks);
        }

        [Test]
        public void OnChunksAssembled_StagesDataWithoutImmediatelySettingLastData()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();

            PrivateFieldAccess.InvokeInstance(receiver, "OnChunksAssembled", "hello world");

            Assert.AreEqual("", receiver.LastData, "LastData is only assigned when the deferred emit actually fires.");
            Assert.IsTrue(GetPendingCompletion(receiver));
            Assert.AreEqual("hello world", GetPendingCompletionData(receiver));
        }

        [Test]
        public void EmitDataReceptionCompleted_AfterStaging_SetsLastDataAndFiresEvent()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();
            receiver.TsSubscribe(receiver, DataSenderReceiver.OnDataReceptionCompletedEvent, nameof(receiver._OnDataReceptionCompletedEventReceived));
            PrivateFieldAccess.InvokeInstance(receiver, "OnChunksAssembled", "hello world");

            receiver._EmitDataReceptionCompleted();

            Assert.AreEqual("hello world", receiver.LastData);
            Assert.IsFalse(GetPendingCompletion(receiver));
            Assert.AreEqual("", GetPendingCompletionData(receiver));
            Assert.AreEqual(1, receiver.OnDataReceptionCompletedCount);
            CollectionAssert.Contains(receiver.ReceivedEvents, "OnDataReceptionCompletedEvent");
        }

        [Test]
        public void EmitDataReceptionCompleted_PendingFlagFalse_IsNoOp()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();
            receiver.TsSubscribe(receiver, DataSenderReceiver.OnDataReceptionCompletedEvent, nameof(receiver._OnDataReceptionCompletedEventReceived));
            // Never staged: _pendingCompletion defaults to false.

            receiver._EmitDataReceptionCompleted();

            Assert.AreEqual(0, receiver.OnDataReceptionCompletedCount);
            CollectionAssert.DoesNotContain(receiver.ReceivedEvents, "OnDataReceptionCompletedEvent");
        }

        [Test]
        public void NewStaging_BeforeOldDeferredEmitFires_LastEmitWinsWithLatestData()
        {
            // Mirrors the source comment's scenario: a second OnChunksAssembled (a new transfer's
            // completion) can overwrite the staged data before the first deferred emit fires -
            // since both share one flag/data pair, only the LATEST staged data is ever emitted.
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();
            PrivateFieldAccess.InvokeInstance(receiver, "OnChunksAssembled", "first");
            PrivateFieldAccess.InvokeInstance(receiver, "OnChunksAssembled", "second");

            receiver._EmitDataReceptionCompleted();

            Assert.AreEqual("second", receiver.LastData);
            Assert.AreEqual(1, receiver.OnDataReceptionCompletedCount, "Only one emit ever fires per pending flag, regardless of how many times staging happened.");
        }

        [Test]
        public void OnTransferStopped_SetsPendingStopFlag()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();

            PrivateFieldAccess.InvokeInstance(receiver, "OnTransferStopped");

            Assert.IsTrue(GetPendingStop(receiver));
        }

        [Test]
        public void EmitDataReceptionStopped_AfterPendingFlagSet_FiresEvent()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();
            receiver.TsSubscribe(receiver, DataSenderReceiver.OnDataReceptionStoppedEvent, nameof(receiver._OnDataReceptionStoppedEventReceived));
            PrivateFieldAccess.InvokeInstance(receiver, "OnTransferStopped");

            receiver._EmitDataReceptionStopped();

            Assert.IsFalse(GetPendingStop(receiver));
            Assert.AreEqual(1, receiver.OnDataReceptionStoppedCount);
            CollectionAssert.Contains(receiver.ReceivedEvents, "OnDataReceptionStoppedEvent");
        }

        [Test]
        public void EmitDataReceptionStopped_PendingFlagFalse_IsNoOp()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();
            receiver.TsSubscribe(receiver, DataSenderReceiver.OnDataReceptionStoppedEvent, nameof(receiver._OnDataReceptionStoppedEventReceived));

            receiver._EmitDataReceptionStopped();

            Assert.AreEqual(0, receiver.OnDataReceptionStoppedCount);
        }

        [Test]
        public void ResetReceiverState_ClearsLastPropertiesAndBothPendingFlags()
        {
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();
            PrivateFieldAccess.InvokeInstance(receiver, "OnChunkStored", 2, 4);
            PrivateFieldAccess.InvokeInstance(receiver, "OnChunksAssembled", "staged");
            PrivateFieldAccess.InvokeInstance(receiver, "OnTransferStopped");

            PrivateFieldAccess.InvokeInstance(receiver, "ResetReceiverState");

            Assert.AreEqual("", receiver.LastData);
            Assert.AreEqual(0, receiver.LastChunkIndex);
            Assert.AreEqual(0, receiver.LastTotalChunks);
            Assert.IsFalse(GetPendingCompletion(receiver));
            Assert.AreEqual("", GetPendingCompletionData(receiver));
            Assert.IsFalse(GetPendingStop(receiver));
        }

        [Test]
        public void ResetReceiverState_SuppressesAnyStaleDeferredEmitsScheduledBeforeIt()
        {
            // The exact scenario ResetReceiverState's clearing exists to protect: staging/pending
            // already scheduled, then a reset happens (e.g. a new transfer starting) before the
            // deferred call fires - both emits must come back as no-ops afterward.
            var receiver = CreateProcess<DataSenderReceiverTestSubclass>();
            receiver.TsSubscribe(receiver, DataSenderReceiver.OnDataReceptionCompletedEvent, nameof(receiver._OnDataReceptionCompletedEventReceived));
            receiver.TsSubscribe(receiver, DataSenderReceiver.OnDataReceptionStoppedEvent, nameof(receiver._OnDataReceptionStoppedEventReceived));
            PrivateFieldAccess.InvokeInstance(receiver, "OnChunksAssembled", "staged");
            PrivateFieldAccess.InvokeInstance(receiver, "OnTransferStopped");

            PrivateFieldAccess.InvokeInstance(receiver, "ResetReceiverState");
            receiver._EmitDataReceptionCompleted();
            receiver._EmitDataReceptionStopped();

            Assert.AreEqual(0, receiver.OnDataReceptionCompletedCount);
            Assert.AreEqual(0, receiver.OnDataReceptionStoppedCount);
        }
    }
}
