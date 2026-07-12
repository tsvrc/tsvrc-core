using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    public class HeadClipGuardAllocateArraysTests : HeadClipGuardTestBase
    {
        [Test]
        public void AllocateArrays_EvenlyDivisibleByBatchFrames_ExactBatchSize()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_batchFrames", 4);

            Invoke(guard, "AllocateArrays", 8);

            Assert.AreEqual(2, GetField<int>(guard, "_batchSize"));
        }

        [Test]
        public void AllocateArrays_NotEvenlyDivisible_CeilingDivision()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_batchFrames", 4);

            Invoke(guard, "AllocateArrays", 9);

            Assert.AreEqual(3, GetField<int>(guard, "_batchSize"));
        }

        [Test]
        public void AllocateArrays_ZeroCount_BatchSizeZero_ArraysAllocatedNotNull()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_batchFrames", 4);

            Invoke(guard, "AllocateArrays", 0);

            Assert.AreEqual(0, GetField<int>(guard, "_batchSize"));
            Assert.AreEqual(0, GetField<Vector3[]>(guard, "_centers").Length);
            Assert.AreEqual(0, GetField<Quaternion[]>(guard, "_invRotations").Length);
            Assert.AreEqual(0, GetField<Vector3[]>(guard, "_marginHalfExtents").Length);
            Assert.AreEqual(0, GetField<Vector3[]>(guard, "_aabbMin").Length);
            Assert.AreEqual(0, GetField<Vector3[]>(guard, "_aabbMax").Length);
            Assert.AreEqual(0, GetField<int[]>(guard, "_candidatePos").Length);
            Assert.AreEqual(0, GetField<int[]>(guard, "_candidateIndices").Length);
        }

        [TestCase(0)]
        [TestCase(-3)]
        public void AllocateArrays_BatchFramesBelowOne_ClampedToOne(int batchFrames)
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_batchFrames", batchFrames);

            Invoke(guard, "AllocateArrays", 5);

            Assert.AreEqual(1, GetField<int>(guard, "_batchFrames"));
            Assert.AreEqual(5, GetField<int>(guard, "_batchSize"));
        }

        [Test]
        public void AllocateArrays_ResetsBatchStartToZero()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_batchStart", 7);

            Invoke(guard, "AllocateArrays", 3);

            Assert.AreEqual(0, GetField<int>(guard, "_batchStart"));
        }
    }
}
