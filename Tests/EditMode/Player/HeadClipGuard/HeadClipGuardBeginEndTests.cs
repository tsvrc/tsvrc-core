using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    public class HeadClipGuardBeginEndTests : HeadClipGuardTestBase
    {
        [Test]
        public void Begin_NullColliderArray_TreatedAsZeroColliders_BecomesActive()
        {
            HeadClipGuard guard = CreateGuard();

            guard.Begin(null, 5);

            Assert.IsTrue(GetField<bool>(guard, "_active"));
            Assert.AreEqual(0, GetField<int>(guard, "_count"));
        }

        [Test]
        public void Begin_CountGreaterThanArrayLength_ClampedViaMathfMin()
        {
            HeadClipGuard guard = CreateGuard();
            BoxCollider[] colliders =
            {
                CreateBoxCollider("A", Vector3.zero, Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero),
                CreateBoxCollider("B", Vector3.zero, Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero),
            };

            guard.Begin(colliders, 100);

            Assert.AreEqual(2, GetField<int>(guard, "_count"));
        }

        [Test]
        public void Begin_NegativeCount_TreatedAsZeroColliders_NoException()
        {
            HeadClipGuard guard = CreateGuard();
            BoxCollider[] colliders = { CreateBoxCollider("A", Vector3.zero, Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero) };

            Assert.DoesNotThrow(() => guard.Begin(colliders, -3));

            Assert.AreEqual(0, GetField<int>(guard, "_count"));
        }

        [Test]
        public void Begin_NullElementsMixedWithReal_SkippedAndCompacted()
        {
            HeadClipGuard guard = CreateGuard();
            BoxCollider second = CreateBoxCollider("Second", new Vector3(3f, 0f, 0f), Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero);
            BoxCollider[] colliders = { null, second, null };

            guard.Begin(colliders, 3);

            Assert.AreEqual(1, GetField<int>(guard, "_count"));
            Vector3 center = GetField<Vector3[]>(guard, "_centers")[0];
            Assert.AreEqual(new Vector3(3f, 0f, 0f), center);
        }

        [Test]
        public void Begin_LocalPlayerNull_PlayerReadyStaysFalse_ButBecomesActive()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_localPlayer", null);

            guard.Begin(null, 0);

            Assert.IsFalse(GetField<bool>(guard, "_playerReady"));
            Assert.IsTrue(GetField<bool>(guard, "_active"));
        }

        [Test]
        public void End_OnFreshInstance_NoExceptionAndAlreadyAtDefaults()
        {
            HeadClipGuard guard = CreateGuard();

            Assert.DoesNotThrow(() => guard.End());

            Assert.IsFalse(GetField<bool>(guard, "_active"));
            Assert.IsFalse(GetField<bool>(guard, "_playerReady"));
            Assert.IsFalse(GetField<bool>(guard, "_lastViolated"));
        }

        [Test]
        public void End_AfterBegin_ResetsEveryRuntimeAndBakedField()
        {
            HeadClipGuard guard = CreateGuard();
            BoxCollider[] colliders = { CreateBoxCollider("A", Vector3.zero, Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero) };
            guard.Begin(colliders, 1);
            SetField(guard, "_lastHeadPos", new Vector3(1f, 2f, 3f));
            SetField(guard, "_lastSafePlayerPos", new Vector3(4f, 5f, 6f));
            SetField(guard, "_lastViolated", true);

            guard.End();

            Assert.IsFalse(GetField<bool>(guard, "_active"));
            Assert.IsFalse(GetField<bool>(guard, "_lastViolated"));
            Assert.IsFalse(GetField<bool>(guard, "_playerReady"));
            Assert.AreEqual(0, GetField<int>(guard, "_count"));
            Assert.AreEqual(0, GetField<int>(guard, "_batchStart"));
            Assert.AreEqual(0, GetField<int>(guard, "_batchSize"));
            Assert.AreEqual(0, GetField<int>(guard, "_candidateCount"));
            Assert.AreEqual(Vector3.zero, GetField<Vector3>(guard, "_lastHeadPos"));
            Assert.AreEqual(Vector3.zero, GetField<Vector3>(guard, "_lastSafePlayerPos"));
            Assert.IsNull(GetField<Vector3[]>(guard, "_centers"));
            Assert.IsNull(GetField<Quaternion[]>(guard, "_invRotations"));
            Assert.IsNull(GetField<Vector3[]>(guard, "_marginHalfExtents"));
            Assert.IsNull(GetField<Vector3[]>(guard, "_aabbMin"));
            Assert.IsNull(GetField<Vector3[]>(guard, "_aabbMax"));
            Assert.IsNull(GetField<int[]>(guard, "_candidatePos"));
            Assert.IsNull(GetField<int[]>(guard, "_candidateIndices"));
        }

        [Test]
        public void End_LeavesTuningFieldsAndLocalPlayerUntouched()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_margin", 0.25f);
            SetField(guard, "_batchFrames", 7);
            SetField(guard, "_batchScanExpansion", 1.5f);
            SetField(guard, "_movSkip", 0.02f);

            guard.End();

            Assert.AreEqual(0.25f, GetField<float>(guard, "_margin"));
            Assert.AreEqual(7, GetField<int>(guard, "_batchFrames"));
            Assert.AreEqual(1.5f, GetField<float>(guard, "_batchScanExpansion"));
            Assert.AreEqual(0.02f, GetField<float>(guard, "_movSkip"));
        }

        [Test]
        public void Begin_ColliderGameObjectDestroyedAfterward_BakedDataSurvivesIndependently()
        {
            // Begin() stores only plain Vector3/Quaternion values, never a
            // BoxCollider/Transform/GameObject reference - destroying the source
            // collider after baking must not affect the guard at all.
            HeadClipGuard guard = CreateGuard();
            BoxCollider col = CreateBoxCollider("Wall", new Vector3(3f, 1f, 0f), Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero);
            guard.Begin(new[] { col }, 1);

            Vector3 centerBefore = GetField<Vector3[]>(guard, "_centers")[0];

            Object.DestroyImmediate(col.gameObject);

            Vector3 centerAfter = GetField<Vector3[]>(guard, "_centers")[0];
            Assert.AreEqual(centerBefore, centerAfter);
            Assert.DoesNotThrow(() => Invoke(guard, "AdvanceBatch", new Vector3(3f, 1f, 0f)));
        }

        [Test]
        public void Begin_CalledTwiceWithoutEnd_SecondCallFullyReplacesFirst()
        {
            HeadClipGuard guard = CreateGuard();
            BoxCollider[] first = { CreateBoxCollider("A", Vector3.zero, Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero), CreateBoxCollider("B", Vector3.zero, Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero) };
            guard.Begin(first, 2);

            BoxCollider[] second = { CreateBoxCollider("C", new Vector3(9f, 0f, 0f), Quaternion.identity, Vector3.one, Vector3.one, Vector3.zero) };
            guard.Begin(second, 1);

            Assert.AreEqual(1, GetField<int>(guard, "_count"));
            Vector3[] centers = GetField<Vector3[]>(guard, "_centers");
            Assert.AreEqual(1, centers.Length);
            Assert.AreEqual(new Vector3(9f, 0f, 0f), centers[0]);
        }
    }
}
