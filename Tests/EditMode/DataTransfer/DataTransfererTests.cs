using NUnit.Framework;
using Tsvrc.DataTransfer;

namespace Tsvrc.Tests.EditMode
{
    // Covers DataTransferer: the thin top-level class mapping DataSenderReceiver's four
    // all-clients hooks onto its own four public event constants, tested as black-box
    // integration - driving the chain from DataSenderReceiver's own hooks (OnChunkStored,
    // OnChunksAssembled, OnTransferStopped) down through the deferred _Emit* mechanism, up to
    // DataTransferer's own OnTransfer*Event firing and being observed by an external subscriber.
    //
    // As throughout this whole DataTransfer test suite, DataChunkReceiver.OnTransferStarted()
    // crashes on Networking.LocalPlayer in Edit Mode when IsProcessOwner() is true, so every
    // scenario here drives the chain via direct hook invocation rather than TransferData().
    public class DataTransfererTests : DataTransferTestBase
    {
        [Test]
        public void EventConstants_MatchTheirDocumentedStringValues()
        {
            Assert.AreEqual("OnTransferStarted", DataTransferer.OnTransferStartedEvent);
            Assert.AreEqual("OnTransferStopped", DataTransferer.OnTransferStoppedEvent);
            Assert.AreEqual("OnTransferCompleted", DataTransferer.OnTransferCompletedEvent);
            Assert.AreEqual("OnTransferChunk", DataTransferer.OnTransferChunkEvent);
        }

        [Test]
        public void OnDataReceptionStarted_EmitsOnTransferStartedEvent()
        {
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.SubscribeToAllTransferEvents();

            PrivateFieldAccess.InvokeInstance(transferer, "OnDataReceptionStarted");

            Assert.AreEqual(1, transferer.OnTransferStartedEventCount);
            Assert.AreEqual(0, transferer.OnTransferStoppedEventCount);
            Assert.AreEqual(0, transferer.OnTransferCompletedEventCount);
            Assert.AreEqual(0, transferer.OnTransferChunkEventCount);
        }

        [Test]
        public void OnDataReceptionStopped_EmitsOnTransferStoppedEvent()
        {
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.SubscribeToAllTransferEvents();

            PrivateFieldAccess.InvokeInstance(transferer, "OnDataReceptionStopped");

            Assert.AreEqual(1, transferer.OnTransferStoppedEventCount);
            Assert.AreEqual(0, transferer.OnTransferStartedEventCount);
        }

        [Test]
        public void OnDataReceptionCompleted_EmitsOnTransferCompletedEvent()
        {
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.SubscribeToAllTransferEvents();

            PrivateFieldAccess.InvokeInstance(transferer, "OnDataReceptionCompleted");

            Assert.AreEqual(1, transferer.OnTransferCompletedEventCount);
        }

        [Test]
        public void OnDataChunkReceived_EmitsOnTransferChunkEvent()
        {
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.SubscribeToAllTransferEvents();

            PrivateFieldAccess.InvokeInstance(transferer, "OnDataChunkReceived");

            Assert.AreEqual(1, transferer.OnTransferChunkEventCount);
        }

        [Test]
        public void FullCascade_ChunkStoredThroughToOnTransferChunkEvent()
        {
            // DataSenderReceiver's OnChunkStored -> its own OnDataChunkReceived hook + TsEmit ->
            // DataTransferer's OnDataChunkReceived override -> TsEmit(OnTransferChunkEvent).
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.SubscribeToAllTransferEvents();

            PrivateFieldAccess.InvokeInstance(transferer, "OnChunkStored", 2, 5);

            Assert.AreEqual(1, transferer.OnTransferChunkEventCount);
            Assert.AreEqual(2, transferer.LastChunkIndex);
            Assert.AreEqual(5, transferer.LastTotalChunks);
        }

        [Test]
        public void FullCascade_ChunksAssembledThroughDeferredEmitToOnTransferCompletedEvent()
        {
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.SubscribeToAllTransferEvents();
            PrivateFieldAccess.InvokeInstance(transferer, "OnChunksAssembled", "the full message");
            Assert.AreEqual(0, transferer.OnTransferCompletedEventCount, "Completed is deferred - must not fire until the emit actually runs.");

            transferer._EmitDataReceptionCompleted();

            Assert.AreEqual(1, transferer.OnTransferCompletedEventCount);
            Assert.AreEqual("the full message", transferer.LastData);
        }

        [Test]
        public void FullCascade_TransferStoppedThroughDeferredEmitToOnTransferStoppedEvent()
        {
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.SubscribeToAllTransferEvents();
            PrivateFieldAccess.InvokeInstance(transferer, "OnTransferStopped");
            Assert.AreEqual(0, transferer.OnTransferStoppedEventCount);

            transferer._EmitDataReceptionStopped();

            Assert.AreEqual(1, transferer.OnTransferStoppedEventCount);
        }

        [Test]
        public void FullCascade_NewStagingSuppressesStaleDeferredCompletedEmit_NoOnTransferCompletedEvent()
        {
            // The full-chain version of DataSenderReceiver's own suppression test: a
            // ResetReceiverState between staging and the deferred emit must prevent
            // DataTransferer's own OnTransferCompletedEvent from ever firing for the stale data.
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            transferer.SubscribeToAllTransferEvents();
            PrivateFieldAccess.InvokeInstance(transferer, "OnChunksAssembled", "stale");
            PrivateFieldAccess.InvokeInstance(transferer, "ResetReceiverState");

            transferer._EmitDataReceptionCompleted();

            Assert.AreEqual(0, transferer.OnTransferCompletedEventCount);
            Assert.AreEqual("", transferer.LastData);
        }

        [Test]
        public void OnOwnerAbandonedProcess_EmptyTrackedList_DoesNotThrow()
        {
            // DataTransferer's own override is a single base call (kept only for an explanatory
            // comment per the source) - this pins that the whole inherited chain remains
            // Edit-Mode-safe through this class's own override too, using the same
            // empty-tracked-list early-return established throughout this suite for
            // OnOwnerAbandonedProcess (see PlayerTrackerAbandonmentTests.cs).
            var transferer = CreateProcess<DataTransfererTestSubclass>();
            SetTrackedPlayerIds(transferer, new string[0]);

            Assert.DoesNotThrow(() => PrivateFieldAccess.InvokeInstance(transferer, "OnOwnerAbandonedProcess"));
        }
    }
}
