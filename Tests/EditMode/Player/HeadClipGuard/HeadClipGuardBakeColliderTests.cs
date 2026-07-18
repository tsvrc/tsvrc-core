using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    public class HeadClipGuardBakeColliderTests : HeadClipGuardTestBase
    {
        private HeadClipGuard BakeSingle(BoxCollider col, float margin)
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_margin", margin);
            Invoke(guard, "AllocateArrays", 1);
            Invoke(guard, "BakeCollider", col, 0);
            return guard;
        }

        [Test]
        public void BakeCollider_IdentityTransform_CenteredCollider_MatchesRawSizePlusMargin()
        {
            BoxCollider col = CreateBoxCollider("Box", Vector3.zero, Quaternion.identity, Vector3.one, new Vector3(2f, 2f, 2f), Vector3.zero);
            HeadClipGuard guard = BakeSingle(col, 0.05f);

            Vector3 center = GetField<Vector3[]>(guard, "_centers")[0];
            Vector3 mHalf = GetField<Vector3[]>(guard, "_marginHalfExtents")[0];

            Assert.AreEqual(Vector3.zero, center);
            AssertApprox(new Vector3(1.05f, 1.05f, 1.05f), mHalf);
        }

        [Test]
        public void BakeCollider_LocalCenterOffset_WorldCenterViaTransformPoint()
        {
            BoxCollider col = CreateBoxCollider("Box", new Vector3(5f, 0f, 0f), Quaternion.identity, Vector3.one, Vector3.one, new Vector3(1f, 0f, 0f));
            HeadClipGuard guard = BakeSingle(col, 0f);

            Vector3 center = GetField<Vector3[]>(guard, "_centers")[0];

            AssertApprox(new Vector3(6f, 0f, 0f), center);
        }

        [Test]
        public void BakeCollider_RotatedTransform_InverseRotationIsTrueConjugate()
        {
            Quaternion rot = Quaternion.Euler(0f, 37f, 0f);
            BoxCollider col = CreateBoxCollider("Box", Vector3.zero, rot, Vector3.one, Vector3.one, Vector3.zero);
            HeadClipGuard guard = BakeSingle(col, 0f);

            Quaternion invRot = GetField<Quaternion[]>(guard, "_invRotations")[0];

            Assert.AreEqual(-rot.x, invRot.x, 1e-5f);
            Assert.AreEqual(-rot.y, invRot.y, 1e-5f);
            Assert.AreEqual(-rot.z, invRot.z, 1e-5f);
            Assert.AreEqual(rot.w, invRot.w, 1e-5f);

            Vector3 worldPoint = new Vector3(3f, -1f, 2f);
            Vector3 backToWorld = rot * (invRot * worldPoint);
            AssertApprox(worldPoint, backToWorld);
        }

        [Test]
        public void BakeCollider_NonUniformPositiveScale_HalfExtentsScaledPerAxis()
        {
            BoxCollider col = CreateBoxCollider("Box", Vector3.zero, Quaternion.identity, new Vector3(2f, 3f, 4f), Vector3.one, Vector3.zero);
            HeadClipGuard guard = BakeSingle(col, 0f);

            Vector3 mHalf = GetField<Vector3[]>(guard, "_marginHalfExtents")[0];

            AssertApprox(new Vector3(1f, 1.5f, 2f), mHalf);
        }

        [Test]
        public void BakeCollider_NegativeScaleAxis_HalfExtentStaysPositive()
        {
            BoxCollider col = CreateBoxCollider("Box", Vector3.zero, Quaternion.identity, new Vector3(-2f, 1f, 1f), Vector3.one, Vector3.zero);
            HeadClipGuard guard = BakeSingle(col, 0f);

            Vector3 mHalf = GetField<Vector3[]>(guard, "_marginHalfExtents")[0];

            Assert.AreEqual(1f, mHalf.x, 1e-4f);
        }

        [Test]
        public void BakeCollider_RotatedAndScaled_TightAabbMatchesSumOfAbsoluteAxisProjections()
        {
            Quaternion rot = Quaternion.Euler(0f, 45f, 0f);
            BoxCollider col = CreateBoxCollider("Box", Vector3.zero, rot, Vector3.one, new Vector3(2f, 1f, 1f), Vector3.zero);
            HeadClipGuard guard = BakeSingle(col, 0f);

            Vector3 aabbMax = GetField<Vector3[]>(guard, "_aabbMax")[0];

            float hx = 1f, hy = 0.5f, hz = 0.5f;
            Vector3 ex = rot * new Vector3(hx, 0f, 0f);
            Vector3 ey = rot * new Vector3(0f, hy, 0f);
            Vector3 ez = rot * new Vector3(0f, 0f, hz);
            float expectedWx = Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x);
            float expectedWy = Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y);
            float expectedWz = Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z);

            Assert.AreEqual(expectedWx, aabbMax.x, 1e-4f);
            Assert.AreEqual(expectedWy, aabbMax.y, 1e-4f);
            Assert.AreEqual(expectedWz, aabbMax.z, 1e-4f);
        }

        [Test]
        public void BakeCollider_Margin_AddedToBothMarginHalfExtentsAndAabb()
        {
            BoxCollider col = CreateBoxCollider("Box", Vector3.zero, Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero);
            HeadClipGuard guard = BakeSingle(col, 0.2f);

            Vector3 mHalf = GetField<Vector3[]>(guard, "_marginHalfExtents")[0];
            Vector3 aabbMax = GetField<Vector3[]>(guard, "_aabbMax")[0];

            Assert.AreEqual(0.7f, mHalf.x, 1e-4f);
            Assert.AreEqual(0.7f, aabbMax.x, 1e-4f);
        }

        private static void AssertApprox(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance);
            Assert.AreEqual(expected.y, actual.y, tolerance);
            Assert.AreEqual(expected.z, actual.z, tolerance);
        }
    }
}
