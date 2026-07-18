using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // AdvanceBatch takes headPos as a parameter and never touches _localPlayer,
    // so it's fully Edit-Mode-testable with synthetic AABB data.
    public class HeadClipGuardAdvanceBatchTests : HeadClipGuardTestBase
    {
        private HeadClipGuard Setup(int count, int batchFrames, float expansion, Vector3[] mins, Vector3[] maxs)
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_batchFrames", batchFrames);
            SetField(guard, "_batchScanExpansion", expansion);
            Invoke(guard, "AllocateArrays", count);

            Vector3[] aabbMin = GetField<Vector3[]>(guard, "_aabbMin");
            Vector3[] aabbMax = GetField<Vector3[]>(guard, "_aabbMax");
            int[] candidatePos = GetField<int[]>(guard, "_candidatePos");
            for (int i = 0; i < count; i++)
            {
                aabbMin[i] = mins[i];
                aabbMax[i] = maxs[i];
                // AllocateArrays leaves a fresh int[] at its default (0), not -1;
                // only InitCandidates normally establishes the "-1 == not a
                // candidate" invariant AdvanceBatch depends on.
                candidatePos[i] = -1;
            }

            return guard;
        }

        [Test]
        public void AdvanceBatch_ZeroCount_ReturnsExistingCandidateCountImmediately()
        {
            HeadClipGuard guard = Setup(0, 1, 0f, new Vector3[0], new Vector3[0]);
            SetField(guard, "_candidateCount", 5); // fixture-seeded, proves this is a true early return

            int result = (int)Invoke(guard, "AdvanceBatch", Vector3.zero);

            Assert.AreEqual(5, result);
        }

        [Test]
        public void AdvanceBatch_ObbEntersNearRange_AddedToCandidates()
        {
            HeadClipGuard guard = Setup(1, 1, 0.1f,
                new[] { new Vector3(-1f, -1f, -1f) },
                new[] { new Vector3(1f, 1f, 1f) });

            int result = (int)Invoke(guard, "AdvanceBatch", Vector3.zero);

            Assert.AreEqual(1, result);
            Assert.AreEqual(0, GetField<int[]>(guard, "_candidateIndices")[0]);
            Assert.AreEqual(0, GetField<int[]>(guard, "_candidatePos")[0]);
        }

        [Test]
        public void AdvanceBatch_ObbLeavesNearRange_RemovedViaSwapWithLast()
        {
            HeadClipGuard guard = Setup(2, 1, 0.1f,
                new[] { new Vector3(-1f, -1f, -1f), new Vector3(-1f, -1f, -1f) },
                new[] { new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f) });

            Invoke(guard, "AdvanceBatch", Vector3.zero); // both become candidates

            // Move OBB 0 far away so it leaves range; OBB 1 stays.
            Vector3[] aabbMin = GetField<Vector3[]>(guard, "_aabbMin");
            Vector3[] aabbMax = GetField<Vector3[]>(guard, "_aabbMax");
            aabbMin[0] = new Vector3(100f, 100f, 100f);
            aabbMax[0] = new Vector3(101f, 101f, 101f);
            SetField(guard, "_batchStart", 0);

            int result = (int)Invoke(guard, "AdvanceBatch", Vector3.zero);

            Assert.AreEqual(1, result);
            Assert.AreEqual(1, GetField<int[]>(guard, "_candidateIndices")[0]);
            Assert.AreEqual(0, GetField<int[]>(guard, "_candidatePos")[1]);
            Assert.AreEqual(-1, GetField<int[]>(guard, "_candidatePos")[0]);
        }

        [Test]
        public void AdvanceBatch_RemovingLastLiveSlot_SelfSwapGuardDoesNotCorrupt()
        {
            HeadClipGuard guard = Setup(1, 1, 0.1f,
                new[] { new Vector3(-1f, -1f, -1f) },
                new[] { new Vector3(1f, 1f, 1f) });

            Invoke(guard, "AdvanceBatch", Vector3.zero); // becomes sole candidate, at last slot

            Vector3[] aabbMin = GetField<Vector3[]>(guard, "_aabbMin");
            Vector3[] aabbMax = GetField<Vector3[]>(guard, "_aabbMax");
            aabbMin[0] = new Vector3(100f, 100f, 100f);
            aabbMax[0] = new Vector3(101f, 101f, 101f);
            SetField(guard, "_batchStart", 0);

            Assert.DoesNotThrow(() => Invoke(guard, "AdvanceBatch", Vector3.zero));

            Assert.AreEqual(0, GetField<int>(guard, "_candidateCount"));
            Assert.AreEqual(-1, GetField<int[]>(guard, "_candidatePos")[0]);
        }

        [Test]
        public void AdvanceBatch_ObbOutsideCurrentSlice_UntouchedThisCall()
        {
            // 2 colliders, batchFrames=2 -> batchSize=1, so only index 0 is scanned this call.
            HeadClipGuard guard = Setup(2, 2, 0.1f,
                new[] { new Vector3(-1f, -1f, -1f), new Vector3(-1f, -1f, -1f) },
                new[] { new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f) });

            int result = (int)Invoke(guard, "AdvanceBatch", Vector3.zero);

            Assert.AreEqual(1, result); // only OBB 0 scanned and added this call
            Assert.AreEqual(-1, GetField<int[]>(guard, "_candidatePos")[1]);
        }

        [Test]
        public void AdvanceBatch_BatchStartWrapsToZero_OnlyWhenSliceReachesEnd()
        {
            HeadClipGuard guard = Setup(4, 4, 0.1f,
                new Vector3[4], new Vector3[4]); // batchSize = 1

            Invoke(guard, "AdvanceBatch", Vector3.zero); // scans index 0
            Assert.AreEqual(1, GetField<int>(guard, "_batchStart"));

            Invoke(guard, "AdvanceBatch", Vector3.zero); // scans index 1
            Assert.AreEqual(2, GetField<int>(guard, "_batchStart"));

            Invoke(guard, "AdvanceBatch", Vector3.zero); // scans index 2
            Assert.AreEqual(3, GetField<int>(guard, "_batchStart"));

            Invoke(guard, "AdvanceBatch", Vector3.zero); // scans index 3, reaches count -> wraps
            Assert.AreEqual(0, GetField<int>(guard, "_batchStart"));
        }

        [Test]
        public void AdvanceBatch_CandidateExactlyAtExpandedAabbEdge_TreatedAsNear()
        {
            HeadClipGuard guard = Setup(1, 1, 0.5f,
                new[] { new Vector3(0f, 0f, 0f) },
                new[] { new Vector3(0f, 0f, 0f) });

            // headPos.x + e == aabbMin.x exactly (0.5 + (-0.5) == 0)
            int result = (int)Invoke(guard, "AdvanceBatch", new Vector3(-0.5f, 0f, 0f));

            Assert.AreEqual(1, result);
        }

        [Test]
        public void AdvanceBatch_FullCycle_SweepsEveryObbExactlyOnce()
        {
            HeadClipGuard guard = Setup(5, 2, 0.1f, // batchSize = ceil(5/2) = 3
                new[] { new Vector3(-1f, -1f, -1f), new Vector3(-1f, -1f, -1f), new Vector3(-1f, -1f, -1f), new Vector3(-1f, -1f, -1f), new Vector3(-1f, -1f, -1f) },
                new[] { new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f) });

            Invoke(guard, "AdvanceBatch", Vector3.zero); // scans [0,3)
            Assert.AreEqual(3, GetField<int>(guard, "_candidateCount"));

            Invoke(guard, "AdvanceBatch", Vector3.zero); // scans [3,5), wraps after
            Assert.AreEqual(5, GetField<int>(guard, "_candidateCount"));
            Assert.AreEqual(0, GetField<int>(guard, "_batchStart"));

            int[] candidatePos = GetField<int[]>(guard, "_candidatePos");
            for (int i = 0; i < 5; i++)
                Assert.AreNotEqual(-1, candidatePos[i], $"index {i} was never scanned into the candidate set");
        }
    }
}
