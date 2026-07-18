using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // Covers DataChunker's pure logic: message validation and the chunk-splitting math.
    // No network/process state involved - CreateProcess<T> is only used because DataChunker
    // still needs a GameObject to attach to as a component.
    public class DataChunkerTests : DataTransferTestBase
    {
        [Test]
        public void ChunkSizeConstant_Is2500()
        {
            // Pins the constant so a change to it visibly breaks this test rather than silently
            // shifting the per-event byte budget BroadcastDataChunkReceived relies on staying
            // under VRChat's 16 KB network event limit.
            Assert.AreEqual(2500, DataChunkerTestSubclass.ChunkSizeConst);
        }

        [Test]
        public void MaxMessageSizeConstant_Is500000()
        {
            Assert.AreEqual(500000, DataChunkerTestSubclass.MaxMessageSizeConst);
        }

        [Test]
        public void ValidateMessage_Null_WarnsAndReturnsFalse()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();

            LogAssert.Expect(LogType.Warning, "[DataChunkerTestSubclass] Cannot send empty message");
            Assert.IsFalse(chunker.InvokeValidateMessage(null));
        }

        [Test]
        public void ValidateMessage_Empty_WarnsAndReturnsFalse()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();

            LogAssert.Expect(LogType.Warning, "[DataChunkerTestSubclass] Cannot send empty message");
            Assert.IsFalse(chunker.InvokeValidateMessage(""));
        }

        [Test]
        public void ValidateMessage_NonEmptyWithinLimit_ReturnsTrue()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();

            Assert.IsTrue(chunker.InvokeValidateMessage("hello world"));
        }

        [Test]
        public void ValidateMessage_ExactlyMaxMessageSize_ReturnsTrue()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();
            string message = BuildString(DataChunkerTestSubclass.MaxMessageSizeConst);

            Assert.IsTrue(chunker.InvokeValidateMessage(message));
        }

        [Test]
        public void ValidateMessage_OneOverMaxMessageSize_LogsErrorAndReturnsFalse()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();
            string message = BuildString(DataChunkerTestSubclass.MaxMessageSizeConst + 1);

            LogAssert.Expect(LogType.Error, $"[DataChunkerTestSubclass] Message too large: {message.Length} chars (max {DataChunkerTestSubclass.MaxMessageSizeConst})");
            Assert.IsFalse(chunker.InvokeValidateMessage(message));
        }

        [Test]
        public void CalculateTotalChunks_ExactMultiple_NoExtraEmptyChunk()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();

            Assert.AreEqual(2, chunker.InvokeCalculateTotalChunks(DataChunkerTestSubclass.ChunkSizeConst * 2));
        }

        [Test]
        public void CalculateTotalChunks_OneCharOver_RoundsUpToExtraChunk()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();

            Assert.AreEqual(3, chunker.InvokeCalculateTotalChunks(DataChunkerTestSubclass.ChunkSizeConst * 2 + 1));
        }

        [Test]
        public void CalculateTotalChunks_SingleChar_OneChunk()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();

            Assert.AreEqual(1, chunker.InvokeCalculateTotalChunks(1));
        }

        [Test]
        public void CalculateTotalChunks_Zero_ZeroChunks()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();

            Assert.AreEqual(0, chunker.InvokeCalculateTotalChunks(0));
        }

        [Test]
        public void ExtractChunk_FullSizeChunk_ReturnsExactSlice()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 2, 'a') ;
            // Make the two halves distinguishable.
            data = BuildString(DataChunkerTestSubclass.ChunkSizeConst, 'a') + BuildString(DataChunkerTestSubclass.ChunkSizeConst, 'b');

            string chunk0 = chunker.InvokeExtractChunk(data, 0);
            string chunk1 = chunker.InvokeExtractChunk(data, 1);

            Assert.AreEqual(DataChunkerTestSubclass.ChunkSizeConst, chunk0.Length);
            Assert.AreEqual(DataChunkerTestSubclass.ChunkSizeConst, chunk1.Length);
            StringAssert.DoesNotContain("b", chunk0);
            StringAssert.DoesNotContain("a", chunk1);
        }

        [Test]
        public void ExtractChunk_LastChunkShorterThanChunkSize_ReturnsShortSlice()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst + 100);

            string lastChunk = chunker.InvokeExtractChunk(data, 1);

            Assert.AreEqual(100, lastChunk.Length);
        }

        [Test]
        public void CreateDataChunks_ExactMultiple_EveryChunkFullSizeNoTrailingEmpty()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst * 3);

            string[] chunks = chunker.InvokeCreateDataChunks(data);

            Assert.AreEqual(3, chunks.Length);
            foreach (string chunk in chunks)
                Assert.AreEqual(DataChunkerTestSubclass.ChunkSizeConst, chunk.Length);
        }

        [Test]
        public void CreateDataChunks_SingleChar_OneChunkOfLengthOne()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();

            string[] chunks = chunker.InvokeCreateDataChunks("x");

            Assert.AreEqual(1, chunks.Length);
            Assert.AreEqual("x", chunks[0]);
        }

        [Test]
        public void CreateDataChunks_LastChunkShorter_SlicingIsCorrectAndRoundTrips()
        {
            var chunker = CreateProcess<DataChunkerTestSubclass>();
            string data = BuildString(DataChunkerTestSubclass.ChunkSizeConst, 'a') + "tail";

            string[] chunks = chunker.InvokeCreateDataChunks(data);

            Assert.AreEqual(2, chunks.Length);
            Assert.AreEqual(DataChunkerTestSubclass.ChunkSizeConst, chunks[0].Length);
            Assert.AreEqual("tail", chunks[1]);
            Assert.AreEqual(data, chunks[0] + chunks[1]);
        }

        [Test]
        public void CreateDataChunks_FullSizeMessage_Produces200ChunksAndRoundTrips()
        {
            // Pins the documented "500,000-char message -> exactly 200 chunks" fact.
            var chunker = CreateProcess<DataChunkerTestSubclass>();
            string data = BuildString(DataChunkerTestSubclass.MaxMessageSizeConst);

            string[] chunks = chunker.InvokeCreateDataChunks(data);

            Assert.AreEqual(200, chunks.Length);
            string rebuilt = string.Join("", chunks);
            Assert.AreEqual(data, rebuilt);
        }
    }
}
