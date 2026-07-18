using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Only the _localPlayer == null path is reachable in Edit Mode - VRCPlayerApi
    // has no public constructor and Tsvrc.Tests.EditMode.asmdef doesn't reference
    // VRCSDKBase.dll. The valid-player path lives in
    // Tests/PlayMode/Player/HeadClipGuard/.
    public class HeadClipGuardInitCandidatesTests : HeadClipGuardTestBase
    {
        [Test]
        public void InitCandidates_LocalPlayerNull_EarlyReturn_NoCandidates()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_localPlayer", null);
            Invoke(guard, "AllocateArrays", 3);

            Invoke(guard, "InitCandidates");

            Assert.AreEqual(0, GetField<int>(guard, "_candidateCount"));
            Assert.AreEqual(Vector3.zero, GetField<Vector3>(guard, "_lastHeadPos"));

            int[] candidatePos = GetField<int[]>(guard, "_candidatePos");
            foreach (int pos in candidatePos)
                Assert.AreEqual(-1, pos);
        }

        [Test]
        public void InitCandidates_ZeroCollidersAndNullPlayer_NoIndexOutOfRange()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_localPlayer", null);
            Invoke(guard, "AllocateArrays", 0);

            Assert.DoesNotThrow(() => Invoke(guard, "InitCandidates"));
            Assert.AreEqual(0, GetField<int>(guard, "_candidateCount"));
        }

        [Test]
        public void InitCandidates_ResetsBatchStartToZero()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_localPlayer", null);
            Invoke(guard, "AllocateArrays", 2);
            SetField(guard, "_batchStart", 1);

            Invoke(guard, "InitCandidates");

            Assert.AreEqual(0, GetField<int>(guard, "_batchStart"));
        }
    }
}
