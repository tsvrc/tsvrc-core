using NUnit.Framework;
using Tsvrc.DataTransfer;

namespace Tsvrc.Tests.Editor
{
    // Covers DataSender: the whole-transfer (not per-chunk) broadcast layer built on
    // ChunkedTransferSession's OnChunkSequence*/OnDataChunkSendRequested hooks. Tested through
    // DataSenderTestSubclass directly (not layered with DataChunkReceiver), so
    // OnDataChunkSendRequested stays an empty no-op - chunk ACKs are simulated via SetReady()
    // directly, isolating this layer's own concern: the Started/Stopped/Completed broadcast
    // events, their deferred-emission mechanism, and the owner-authentication guard.
    public class DataSenderTests : DataTransferTestBase
    {
        [Test]
        public void TransferStarted_SingleChunkTransfer_FiresHookAndEmitsEventImmediately()
        {
            // Unlike Stopped/Completed, Started is not deferred - it fires and emits inline.
            var sender = CreateProcess<DataSenderTestSubclass>();
            SeedAsOwner(sender);

            sender.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });

            Assert.AreEqual(1, sender.OnTransferStartedCount);
        }

        [Test]
        public void TransferStarted_MultiChunkTransfer_FiresOnlyOnceNotPerChunk()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();
            SeedAsOwner(sender);
            string ownerId = "TestOwner#" + OwnerPlayerId;
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2);
            sender.TransferData(data, new[] { ownerId });
            Assert.AreEqual(1, sender.OnTransferStartedCount);

            sender.SetReady(); // completes chunk 1, enters the inter-chunk gap
            SeedAsOwner(sender); // InternalCleanup zeroed ownership; re-seed for the manual continuation
            sender.StartReadyCheck(new[] { ownerId }); // manually continue to chunk 2 (Play-Mode-only via _StartNextReadyCheck otherwise)

            Assert.AreEqual(1, sender.OnTransferStartedCount, "Chunk 2 starting its own ready check must not re-fire the whole-transfer started event.");
        }

        [Test]
        public void TransferCompleted_SingleChunkTransfer_SetsPendingFlagAndDefersEmit()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();
            SeedAsOwner(sender);
            sender.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });

            sender.SetReady(); // completes the only chunk

            Assert.AreEqual(1, sender.OnTransferCompletedCount);
            Assert.IsTrue(GetPendingTransferCompleted(sender));
            CollectionAssert.DoesNotContain(sender.ReceivedEvents, "OnDataTransferCompletedEvent");
        }

        [Test]
        public void EmitDataTransferCompleted_AfterPendingFlagSet_FiresTheEvent()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();
            SeedAsOwner(sender);
            sender.TsSubscribe(sender, DataSender.OnDataTransferCompletedEvent, nameof(sender._OnDataTransferCompletedEventReceived));
            sender.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId });
            sender.SetReady();

            sender._EmitDataTransferCompleted(); // simulates the deferred call actually firing

            CollectionAssert.Contains(sender.ReceivedEvents, "OnDataTransferCompletedEvent");
            Assert.IsFalse(GetPendingTransferCompleted(sender));
        }

        [Test]
        public void EmitDataTransferCompleted_PendingFlagAlreadyFalse_IsNoOp()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();
            sender.TsSubscribe(sender, DataSender.OnDataTransferCompletedEvent, nameof(sender._OnDataTransferCompletedEventReceived));
            // Never triggered a completion: the pending flag defaults to false.

            sender._EmitDataTransferCompleted();

            CollectionAssert.DoesNotContain(sender.ReceivedEvents, "OnDataTransferCompletedEvent");
        }

        [Test]
        public void TransferStopped_SetsPendingFlagAndDefersEmit()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();
            SeedAsOwner(sender);
            sender.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId, "Other#1" }); // stays running

            sender.CancelDataTransfer();

            Assert.AreEqual(1, sender.OnTransferStoppedCount);
            Assert.IsTrue(GetPendingTransferStopped(sender));
            CollectionAssert.DoesNotContain(sender.ReceivedEvents, "OnDataTransferStoppedEvent");
        }

        [Test]
        public void EmitDataTransferStopped_AfterPendingFlagSet_FiresTheEvent()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();
            SeedAsOwner(sender);
            sender.TsSubscribe(sender, DataSender.OnDataTransferStoppedEvent, nameof(sender._OnDataTransferStoppedEventReceived));
            sender.TransferData("hello", new[] { "TestOwner#" + OwnerPlayerId, "Other#1" });
            sender.CancelDataTransfer();

            sender._EmitDataTransferStopped();

            CollectionAssert.Contains(sender.ReceivedEvents, "OnDataTransferStoppedEvent");
            Assert.IsFalse(GetPendingTransferStopped(sender));
        }

        [Test]
        public void EmitDataTransferStopped_PendingFlagAlreadyFalse_IsNoOp()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();
            sender.TsSubscribe(sender, DataSender.OnDataTransferStoppedEvent, nameof(sender._OnDataTransferStoppedEventReceived));

            sender._EmitDataTransferStopped();

            CollectionAssert.DoesNotContain(sender.ReceivedEvents, "OnDataTransferStoppedEvent");
        }

        [Test]
        public void NewTransferStart_ClearsStalePendingStoppedFlag_SuppressesTheOldDeferredEmit()
        {
            // Pins the exact scenario the source comment describes: a stopped notification's
            // deferred emit is still pending when a brand new transfer starts before it fires -
            // the new transfer's own start must suppress the stale one.
            var sender = CreateProcess<DataSenderTestSubclass>();
            SeedAsOwner(sender);
            sender.TsSubscribe(sender, DataSender.OnDataTransferStoppedEvent, nameof(sender._OnDataTransferStoppedEventReceived));
            sender.TransferData("first", new[] { "TestOwner#" + OwnerPlayerId, "Other#1" });
            sender.CancelDataTransfer();
            Assert.IsTrue(GetPendingTransferStopped(sender));

            SeedAsOwner(sender); // re-seed after InternalCleanup zeroed ownership
            sender.TransferData("second", new[] { "TestOwner#" + OwnerPlayerId });
            Assert.IsFalse(GetPendingTransferStopped(sender), "Starting a new transfer must clear the old one's stale pending flag.");

            sender._EmitDataTransferStopped(); // the old transfer's deferred call, finally firing late

            CollectionAssert.DoesNotContain(sender.ReceivedEvents, "OnDataTransferStoppedEvent",
                "A stale deferred stop must never emit after a new transfer has already started.");
        }

        [Test]
        public void NewTransferStart_ClearsStalePendingCompletedFlag_SuppressesTheOldDeferredEmit()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();
            SeedAsOwner(sender);
            sender.TsSubscribe(sender, DataSender.OnDataTransferCompletedEvent, nameof(sender._OnDataTransferCompletedEventReceived));
            sender.TransferData("first", new[] { "TestOwner#" + OwnerPlayerId });
            sender.SetReady(); // completes the single-chunk transfer
            Assert.IsTrue(GetPendingTransferCompleted(sender));

            SeedAsOwner(sender);
            sender.TransferData("second", new[] { "TestOwner#" + OwnerPlayerId });
            Assert.IsFalse(GetPendingTransferCompleted(sender));

            sender._EmitDataTransferCompleted();

            CollectionAssert.DoesNotContain(sender.ReceivedEvents, "OnDataTransferCompletedEvent");
        }

        [Test]
        public void NotifyTrackedPlayersDataTransferStarted_DirectCallWithoutBroadcastingFlag_RejectedByCallerGuard()
        {
            // NetworkCalling.CallingPlayer reads null outside a real dispatch, so the guard's
            // `caller == null` branch is what actually gets exercised here.
            var sender = CreateProcess<DataSenderTestSubclass>();
            // _isBroadcasting left at its default false.

            Assert.DoesNotThrow(() => sender.NotifyTrackedPlayersDataTransferStarted());

            Assert.AreEqual(0, sender.OnTransferStartedCount);
        }

        [Test]
        public void NotifyTrackedPlayersDataTransferStopped_DirectCallWithoutBroadcastingFlag_RejectedByCallerGuard()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();

            Assert.DoesNotThrow(() => sender.NotifyTrackedPlayersDataTransferStopped());

            Assert.AreEqual(0, sender.OnTransferStoppedCount);
            Assert.IsFalse(GetPendingTransferStopped(sender));
        }

        [Test]
        public void NotifyTrackedPlayersDataTransferCompleted_DirectCallWithoutBroadcastingFlag_RejectedByCallerGuard()
        {
            var sender = CreateProcess<DataSenderTestSubclass>();

            Assert.DoesNotThrow(() => sender.NotifyTrackedPlayersDataTransferCompleted());

            Assert.AreEqual(0, sender.OnTransferCompletedCount);
            Assert.IsFalse(GetPendingTransferCompleted(sender));
        }
    }
}
