using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    public class HeadClipGuardComputePushLocalTests : HeadClipGuardTestBase
    {
        private HeadClipGuard Guard(float margin)
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_margin", margin);
            return guard;
        }

        private static Vector3 Push(HeadClipGuard guard, Vector3 headLocal, Vector3 capsuleLocal, Vector3 mHalf) =>
            (Vector3)Invoke(guard, "ComputePushLocal", headLocal, capsuleLocal, mHalf);

        [Test]
        public void CapsuleExceedsOnXOnly_PositiveSide_PushResolvesOnXOnly()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f); // raw half-extents 0.95
            Vector3 capsuleLocal = new Vector3(1.0f, 0f, 0f); // exceeds raw X by 0.05
            Vector3 headLocal = new Vector3(0.5f, 0f, 0f);

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreEqual(mHalf.x + 0.001f - headLocal.x, push.x, 1e-5f);
            Assert.AreEqual(0f, push.y, 1e-5f);
            Assert.AreEqual(0f, push.z, 1e-5f);
        }

        [Test]
        public void CapsuleExceedsOnXOnly_NegativeSide_PushIsNegative()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f);
            Vector3 capsuleLocal = new Vector3(-1.0f, 0f, 0f);
            Vector3 headLocal = new Vector3(-0.5f, 0f, 0f);

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreEqual(-(mHalf.x + 0.001f) - headLocal.x, push.x, 1e-5f);
        }

        [Test]
        public void CapsuleExceedsOnYOnly_PushResolvesOnYOnly()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f);
            Vector3 capsuleLocal = new Vector3(0f, 1.0f, 0f);
            Vector3 headLocal = new Vector3(0f, 0.5f, 0f);

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreEqual(0f, push.x, 1e-5f);
            Assert.AreEqual(mHalf.y + 0.001f - headLocal.y, push.y, 1e-5f);
            Assert.AreEqual(0f, push.z, 1e-5f);
        }

        [Test]
        public void CapsuleExceedsOnZOnly_PushResolvesOnZOnly()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f);
            Vector3 capsuleLocal = new Vector3(0f, 0f, 1.0f);
            Vector3 headLocal = new Vector3(0f, 0f, 0.5f);

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreEqual(0f, push.x, 1e-5f);
            Assert.AreEqual(0f, push.y, 1e-5f);
            Assert.AreEqual(mHalf.z + 0.001f - headLocal.z, push.z, 1e-5f);
        }

        [Test]
        public void TiedExceedance_XAndY_PicksXByPriority()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f);
            Vector3 capsuleLocal = new Vector3(1.0f, 1.0f, 0f); // equal exceedance on X and Y
            Vector3 headLocal = new Vector3(0.5f, 0.5f, 0f);

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreNotEqual(0f, push.x);
            Assert.AreEqual(0f, push.y, 1e-5f);
        }

        [Test]
        public void TiedExceedance_YAndZ_PicksYByPriority()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f);
            Vector3 capsuleLocal = new Vector3(0f, 1.0f, 1.0f); // equal exceedance on Y and Z, X not exceeding
            Vector3 headLocal = new Vector3(0f, 0.5f, 0.5f);

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreNotEqual(0f, push.y);
            Assert.AreEqual(0f, push.z, 1e-5f);
        }

        [Test]
        public void DegeneratePath_CapsuleAlsoInsideRawBox_PicksHeadsTrueNearestFace()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f); // raw half-extents 0.95
            Vector3 capsuleLocal = new Vector3(0.01f, 0.01f, 0.01f); // well within raw box on every axis
            Vector3 headLocal = new Vector3(0.99f, 0f, 0f); // head nearest to +X face

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreNotEqual(0f, push.x);
            Assert.AreEqual(0f, push.y, 1e-5f);
            Assert.AreEqual(0f, push.z, 1e-5f);
            float px = mHalf.x - Mathf.Abs(headLocal.x);
            Assert.AreEqual(px + 0.001f, push.x, 1e-4f);
        }

        [Test]
        public void DegeneratePath_TiedPenetrationDepth_PicksXOverYOverZ()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f);
            Vector3 capsuleLocal = new Vector3(0.01f, 0.01f, 0.01f);
            Vector3 headLocal = new Vector3(0.5f, 0.5f, 0.5f); // equal penetration depth on every axis

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreNotEqual(0f, push.x);
            Assert.AreEqual(0f, push.y, 1e-5f);
            Assert.AreEqual(0f, push.z, 1e-5f);
        }

        [Test]
        public void ExceedanceExactlyZero_TreatedAsNotExceeding_FallsThroughToDegeneratePath()
        {
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f); // raw half-extent 0.95
            Vector3 capsuleLocal = new Vector3(0.95f, 0f, 0f); // exceedance == 0 exactly on X
            Vector3 headLocal = new Vector3(0.99f, 0f, 0f);

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            // Degenerate path: picks head's own nearest face (X here too, since head is
            // closest to the X wall), proving exceedance==0 did not take the primary branch.
            float px = mHalf.x - Mathf.Abs(headLocal.x);
            Assert.AreEqual(px + 0.001f, push.x, 1e-4f);
        }

        [Test]
        public void EntryFaceHeuristic_CanProduceMuchLargerPushThanHeadsTrueNearestFace()
        {
            // The primary path is driven by the capsule's exceedance, not the head's
            // own nearest face, and can therefore choose a far larger push than necessary.
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f); // raw half-extents 0.95
            Vector3 capsuleLocal = new Vector3(0.01f, 1.0f, 0.01f); // pressed against the raw box on Y
            Vector3 headLocal = new Vector3(0.99f, 0f, 0f); // head is actually right at the X wall

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreEqual(0f, push.x, 1e-5f);
            Assert.AreNotEqual(0f, push.y);
            Assert.Greater(Mathf.Abs(push.y), Mathf.Abs(mHalf.x + 0.001f - headLocal.x) * 10f,
                "expected the entry-face heuristic to pick a far larger push than the head's true nearest exit");
        }

        [Test]
        public void SameGeometryWithCapsuleInsideRawBox_DegeneratePathPicksTrueMinimalPush()
        {
            // Same head position as the test above, but with the capsule moved inside
            // the raw box so the degenerate path runs instead - proves it correctly
            // recovers the true minimal push where the primary path does not.
            HeadClipGuard guard = Guard(0.05f);
            Vector3 mHalf = new Vector3(1f, 1f, 1f);
            Vector3 capsuleLocal = new Vector3(0.01f, 0.01f, 0.01f);
            Vector3 headLocal = new Vector3(0.99f, 0f, 0f);

            Vector3 push = Push(guard, headLocal, capsuleLocal, mHalf);

            Assert.AreNotEqual(0f, push.x);
            Assert.AreEqual(0f, push.y, 1e-5f);
            float px = mHalf.x - Mathf.Abs(headLocal.x);
            Assert.AreEqual(px + 0.001f, push.x, 1e-4f);
        }
    }
}
