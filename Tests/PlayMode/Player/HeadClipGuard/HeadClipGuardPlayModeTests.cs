using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Player.HeadClipGuard
{
    // PostLateUpdate's entire push/teleport logic operates on a real VRCPlayerApi's
    // GetTrackingData(Head)/GetPosition/GetRotation/TeleportTo - none of which resolve in Edit
    // Mode. ClientSim never organically dispatches PostLateUpdate to a plain UdonSharpBehaviour,
    // so it is invoked directly below, same as every other such callback in this codebase.
    // A live ClientSim local player's head tracking data proved real and stable (derived from
    // the capsule position) rather than null/zero, so no new framework primitive was needed here.
    public class HeadClipGuardPlayModeTests : TsPlayModeTestBase
    {
        private GameObject _guardGameObject;
        private GameObject _colliderGameObject;

        [TearDown]
        public void TearDown()
        {
            if (_guardGameObject != null) Object.DestroyImmediate(_guardGameObject);
            if (_colliderGameObject != null) Object.DestroyImmediate(_colliderGameObject);
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_RealHeadInsideViolatingCollider_TeleportsPlayerOut()
        {
            yield return StartClientSim();
            VRCPlayerApi local = Networking.LocalPlayer;
            Vector3 realHeadPos = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 posBefore = local.GetPosition();

            _colliderGameObject = new GameObject("ViolatingCollider");
            var collider = _colliderGameObject.AddComponent<BoxCollider>();
            collider.transform.position = realHeadPos;
            collider.size = Vector3.one * 2f;

            _guardGameObject = new GameObject(nameof(HeadClipGuardPlayModeTests));
            var guard = _guardGameObject.AddComponent<Tsvrc.Player.HeadClipGuard>();
            guard.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            guard.Begin(new[] { collider }, 1);
            // Bypasses the movement gate for this first call regardless of the real spawn
            // position, since PostLateUpdate skips all work when the head hasn't moved since
            // the last processed frame.
            PrivateFieldAccess.SetField(guard, "_lastHeadPos", Vector3.zero);

            guard.PostLateUpdate();

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(guard, "_lastViolated"),
                "A collider centered exactly on the real head position must be detected as a real violation.");
            Assert.AreNotEqual(posBefore, local.GetPosition(),
                "A real detected violation must actually teleport the player via VRCPlayerApi.TeleportTo.");
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_RealHeadOutsideAnyCollider_NoTeleport()
        {
            yield return StartClientSim();
            VRCPlayerApi local = Networking.LocalPlayer;
            Vector3 posBefore = local.GetPosition();

            _colliderGameObject = new GameObject("FarAwayCollider");
            var collider = _colliderGameObject.AddComponent<BoxCollider>();
            collider.transform.position = new Vector3(1000f, 1000f, 1000f);
            collider.size = Vector3.one * 2f;

            _guardGameObject = new GameObject(nameof(HeadClipGuardPlayModeTests));
            var guard = _guardGameObject.AddComponent<Tsvrc.Player.HeadClipGuard>();
            guard.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            guard.Begin(new[] { collider }, 1);
            PrivateFieldAccess.SetField(guard, "_lastHeadPos", Vector3.zero);

            guard.PostLateUpdate();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(guard, "_lastViolated"));
            Assert.AreEqual(posBefore, local.GetPosition(),
                "No real violation must mean no teleport call at all.");
        }
    }
}
