using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Player
{
    /// <summary>
    /// Prevents VR head clipping through geometry by teleporting the player back to
    /// their last safe position when the head enters any registered solid OBB.
    /// All collider data is baked at setup time; PostLateUpdate performs only arithmetic.
    /// Usage: Setup(colliders, count) → Activate(). Call Deactivate() to stop.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class HeadClipGuard : TsvrcBehaviour
    {
        [Header("Configuration")]
        [SerializeField, Tooltip("Margin (metres) added to solid OBB half-extents. Prevents head from grazing surfaces.")]
        private float _margin = 0.05f;

        // Baked solid OBB data: head must stay OUTSIDE these
        private Vector3[] _centers;
        private Quaternion[] _invRotations;
        private Vector3[] _halfExtents;
        private Vector3[] _aabbMin; // world-space AABB for fast early-rejection
        private Vector3[] _aabbMax;
        private int _count;

        // Runtime state
        private bool _active;
        private VRCPlayerApi _localPlayer;
        private Vector3 _lastSafePlayerPos;
        private Quaternion _lastSafePlayerRot;

        #region TsvrcBehaviour Callbacks

        protected override void TsStart()
        {
            _localPlayer = Networking.LocalPlayer;
        }

        #endregion

        #region VRChat Callbacks

        /// <summary>
        /// Runs after all IK/camera updates, providing the authoritative head position for this frame.
        /// </summary>
        public override void PostLateUpdate()
        {
            if (!_active) return;
            if (_localPlayer == null || !_localPlayer.IsValid()) return;

            Vector3 headPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            if (IsViolating(headPos))
            {
                // lerpOnRemote=false prevents the camera sweeping through geometry during correction
                _localPlayer.TeleportTo(
                    _lastSafePlayerPos,
                    _lastSafePlayerRot,
                    VRC.SDKBase.VRC_SceneDescriptor.SpawnOrientation.Default,
                    false);
            }
            else
            {
                _lastSafePlayerPos = _localPlayer.GetPosition();
                _lastSafePlayerRot = _localPlayer.GetRotation();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Bakes world-space OBB + AABB data for solid colliders (head must stay outside).
        /// Only the first <paramref name="count"/> entries are baked; supports over-allocated buffers.
        /// Replaces any previously registered colliders.
        /// </summary>
        public void Setup(BoxCollider[] solidColliders, int count)
        {
            int n = solidColliders == null ? 0 : Mathf.Min(count, solidColliders.Length);
            AllocateArrays(n);
            for (int i = 0; i < n; i++)
                BakeCollider(solidColliders[i], i);
        }

        /// <summary>
        /// Starts guarding. Records the current player state as the initial safe position.
        /// Call Setup() before activating.
        /// </summary>
        public void Activate()
        {
            if (_localPlayer == null)
                _localPlayer = Networking.LocalPlayer;

            if (_localPlayer != null && _localPlayer.IsValid())
            {
                _lastSafePlayerPos = _localPlayer.GetPosition();
                _lastSafePlayerRot = _localPlayer.GetRotation();
            }

            _active = true;
        }

        /// <summary> Stops guarding without clearing baked data. </summary>
        public void Deactivate() => _active = false;

        #endregion

        #region Private Methods

        private void AllocateArrays(int n)
        {
            _count = n;
            _centers = new Vector3[n];
            _invRotations = new Quaternion[n];
            _halfExtents = new Vector3[n];
            _aabbMin = new Vector3[n];
            _aabbMax = new Vector3[n];
        }

        private void BakeCollider(BoxCollider col, int i)
        {
            Transform t = col.transform;
            Quaternion rot = t.rotation;
            Vector3 scale = t.lossyScale;

            Vector3 center = t.TransformPoint(col.center);
            Vector3 half = new Vector3(
                col.size.x * Mathf.Abs(scale.x) * 0.5f,
                col.size.y * Mathf.Abs(scale.y) * 0.5f,
                col.size.z * Mathf.Abs(scale.z) * 0.5f);

            _centers[i] = center;
            _invRotations[i] = Quaternion.Inverse(rot);
            _halfExtents[i] = half;

            // Project OBB axes onto world axes to compute the tight world-space AABB
            Vector3 ex = rot * new Vector3(half.x, 0f, 0f);
            Vector3 ey = rot * new Vector3(0f, half.y, 0f);
            Vector3 ez = rot * new Vector3(0f, 0f, half.z);
            Vector3 worldHalf = new Vector3(
                Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x),
                Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y),
                Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z));

            _aabbMin[i] = center - worldHalf;
            _aabbMax[i] = center + worldHalf;
        }

        private bool IsViolating(Vector3 head)
        {
            // Head must stay OUTSIDE each solid OBB
            for (int i = 0; i < _count; i++)
            {
                // AABB early-reject before full OBB test
                Vector3 mn = _aabbMin[i];
                Vector3 mx = _aabbMax[i];
                if (head.x < mn.x || head.x > mx.x ||
                    head.y < mn.y || head.y > mx.y ||
                    head.z < mn.z || head.z > mx.z)
                    continue;

                Vector3 local = _invRotations[i] * (head - _centers[i]);
                Vector3 half = _halfExtents[i];
                if (Mathf.Abs(local.x) <= half.x + _margin &&
                    Mathf.Abs(local.y) <= half.y + _margin &&
                    Mathf.Abs(local.z) <= half.z + _margin)
                    return true;
            }

            return false;
        }

        #endregion
    }
}
