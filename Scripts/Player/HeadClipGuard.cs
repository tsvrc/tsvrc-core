using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Player
{
    /// <summary>
    /// Prevents VR head clipping through geometry by applying the minimum world-space push needed
    /// to move the head out of any violated solid OBB. Runs in PostLateUpdate after all IK and
    /// tracking have settled. Collider data is baked once on Begin().
    ///
    /// PostLateUpdate stages:
    ///   0. Movement gate, skip all work when head movement per axis is below _movSkip and no
    ///                      violation was active last frame. Keeps standing-still frames cheap.
    ///   1. Batch phase, advance a rolling slice of all colliders through a wide AABB each frame;
    ///                      candidates added/removed in O(1) via swap-with-last.
    ///   2. Reject phase, tight AABB pre-check on each candidate before the OBB test.
    ///   3. Test phase, full OBB+margin test on surviving candidates.
    ///   4. Push phase, accumulate minimum push vectors per violated OBB; teleport capsule.
    ///
    /// Forward rotation for the push is derived as the conjugate of the stored inverse rotation:
    ///   q_forward = Quaternion(-ix, -iy, -iz, iw)   (valid for unit quaternions)
    ///
    /// Handles multiple simultaneous violations (e.g. geometry corners) by accumulating push vectors.
    /// Falls back to the last safe capsule position when push vectors cancel exactly
    /// (head trapped symmetrically between two opposing walls, extremely rare).
    ///
    /// Usage: Begin(colliders, count). Call End() to stop.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class HeadClipGuard : TsvrcBehaviour
    {
        [Header("Configuration")]
        [SerializeField, Tooltip("Margin (metres) added to solid OBB half-extents. Keeps the head a small distance from the wall surface and prevents jitter at grazing incidence.")]
        private float _margin = 0.05f;

        [Header("Batching")]
        [SerializeField, Tooltip("Number of frames over which the full collider list is scanned. Each frame processes 1/_batchFrames of the list. Must satisfy: batchScanExpansion >= maxSpeed * batchFrames / minFPS.")]
        private int _batchFrames = 4;
        [SerializeField, Tooltip("Extra AABB expansion (metres) for the batch scan. Default 0.5 covers 5 m/s at 72 fps with 4 batch frames.")]
        private float _batchScanExpansion = 0.5f;

        [Header("Movement Gate")]
        [SerializeField, Tooltip("Per-axis head movement threshold (metres). PostLateUpdate is skipped when all axes are below this and no violation was active. VR tracking jitter is ~1-3 mm; 5 mm skips most standing-still frames.")]
        private float _movSkip = 0.005f;

        // ── Baked OBB data ───────────────────────────────────────────────────────
        // _invRotations[i]      world-to-local rotation for OBB i.
        // _marginHalfExtents[i] OBB half-extents plus _margin.
        // _aabbMin/Max[i]       tight world-space AABB of OBB i, expanded by _margin.
        //                       Serves as both the per-candidate pre-reject bounds and the
        //                       base for the batch scan (expanded inline by _batchScanExpansion).
        private Vector3[] _centers;
        private Quaternion[] _invRotations;
        private Vector3[] _marginHalfExtents;
        private Vector3[] _aabbMin;
        private Vector3[] _aabbMax;
        private int _count;

        // ── Candidate tracking ───────────────────────────────────────────────────
        // _candidatePos[i]  index of OBB i inside _candidateIndices, or -1 if not a candidate.
        // _candidateIndices compact list: [0, _candidateCount) holds the current nearby OBB indices.
        private int[] _candidatePos;
        private int[] _candidateIndices;
        private int _candidateCount;

        // ── Batch state ──────────────────────────────────────────────────────────
        private int _batchStart;
        private int _batchSize; // ceil(_count / _batchFrames)

        // ── Runtime state ────────────────────────────────────────────────────────
        private bool _active;
        private bool _playerReady;  // set after first IsValid() confirmation; skips that call thereafter
        private bool _lastViolated; // whether last processed frame had a violation; bypasses movement gate
        private VRCPlayerApi _localPlayer;
        private Vector3 _lastHeadPos; // head position from the last processed frame; movement gate delta source
        private Vector3 _lastSafePlayerPos; // capsule position from the last non-violated frame; fallback for symmetric-push edge case

        #region TsvrcBehaviour Callbacks

        protected override void TsStart()
        {
            _localPlayer = Networking.LocalPlayer;
        }

        #endregion

        #region VRChat Callbacks

        public override void PostLateUpdate()
        {
            if (!_active) return;

            // Confirm player once; replace per-frame IsValid() with a single bool check.
            if (!_playerReady)
            {
                if (_localPlayer == null || !_localPlayer.IsValid()) return;
                _playerReady = true;
            }

            Vector3 headPos = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            // ── Movement gate ────────────────────────────────────────────────────
            // Skip all work while standing still. Per-axis float deltas avoid any
            // vector allocation or multiply. _lastViolated bypasses the gate so active
            // violations are always corrected every frame.
            if (!_lastViolated)
            {
                float skip = _movSkip;
                float nSkip = -skip;
                float dx = headPos.x - _lastHeadPos.x;
                float dy = headPos.y - _lastHeadPos.y;
                float dz = headPos.z - _lastHeadPos.z;
                if (dx > nSkip && dx < skip &&
                    dy > nSkip && dy < skip &&
                    dz > nSkip && dz < skip)
                    return;
            }
            _lastHeadPos = headPos;

            // ── Batch phase ──────────────────────────────────────────────────────
            // AdvanceBatch returns the current candidate count, avoiding two extra field reads
            // (the zero-check and the cCount assignment) on every processed frame.
            int cCount = AdvanceBatch(headPos);

            if (cCount == 0)
            {
                _lastViolated = false;
                return;
            }

            // ── Test phase ───────────────────────────────────────────────────────
            Vector3 capsulePos = _localPlayer.GetPosition();
            Vector3 totalPush = Vector3.zero;
            bool violated = false;

            // Cache all array references as locals; in Udon each field access is a heap lookup,
            // so one dereference here avoids one per loop iteration across all six arrays.
            int[] candidateIndices = _candidateIndices;
            Vector3[] aabbMin = _aabbMin;
            Vector3[] aabbMax = _aabbMax;
            Vector3[] centers = _centers;
            Quaternion[] invRotations = _invRotations;
            Vector3[] marginHalfExtents = _marginHalfExtents;

            for (int ci = 0; ci < cCount; ci++)
            {
                int i = candidateIndices[ci];

                // Tight AABB pre-reject before the more expensive OBB test.
                Vector3 mn = aabbMin[i];
                Vector3 mx = aabbMax[i];
                if (headPos.x < mn.x || headPos.x > mx.x ||
                    headPos.y < mn.y || headPos.y > mx.y ||
                    headPos.z < mn.z || headPos.z > mx.z)
                    continue;

                // Cache per-candidate data once after the AABB pass; reused below in the push path.
                Vector3 center = centers[i];
                Quaternion invRot = invRotations[i];

                // Full OBB + margin test.
                // |x| > h  written as  (x > h || x < -h)  to stay within native VM comparisons.
                Vector3 headLocal = invRot * (headPos - center);
                Vector3 mHalf = marginHalfExtents[i];
                if ((headLocal.x > mHalf.x || headLocal.x < -mHalf.x) ||
                    (headLocal.y > mHalf.y || headLocal.y < -mHalf.y) ||
                    (headLocal.z > mHalf.z || headLocal.z < -mHalf.z))
                    continue;

                // ── Push phase ───────────────────────────────────────────────────
                violated = true;
                Vector3 capsuleLocal = invRot * (capsulePos - center);
                // Forward rotation = conjugate of invRot: Quaternion(-ix, -iy, -iz, iw).
                totalPush += new Quaternion(-invRot.x, -invRot.y, -invRot.z, invRot.w)
                             * ComputePushLocal(headLocal, capsuleLocal, mHalf);
            }

            _lastViolated = violated;

            if (!violated)
            {
                _lastSafePlayerPos = capsulePos;
                return;
            }

            // GetRotation is deferred to here, only needed when a teleport will actually fire.
            Quaternion capsuleRot = _localPlayer.GetRotation();
            float sqrPush = totalPush.sqrMagnitude;
            _localPlayer.TeleportTo(
                sqrPush > 1e-8f ? capsulePos + totalPush : _lastSafePlayerPos,
                capsuleRot,
                VRC.SDKBase.VRC_SceneDescriptor.SpawnOrientation.Default,
                false);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Bakes collider data and starts guarding. Null elements in the array are skipped.
        /// Records the current player position as the safe fallback.
        /// </summary>
        public void Begin(BoxCollider[] solidColliders, int count)
        {
            if (_localPlayer == null)
                _localPlayer = Networking.LocalPlayer;

            int n = solidColliders == null ? 0 : Mathf.Min(count, solidColliders.Length);

            int validCount = 0;
            for (int i = 0; i < n; i++)
                if (solidColliders[i] != null) validCount++;

            AllocateArrays(validCount);

            int j = 0;
            for (int i = 0; i < n; i++)
                if (solidColliders[i] != null) BakeCollider(solidColliders[i], j++);

            InitCandidates();

            // Reset _playerReady so PostLateUpdate re-validates if the player is transiently
            // invalid at Begin() time (e.g. reuse between sessions).
            _playerReady = false;
            if (_localPlayer != null && _localPlayer.IsValid())
            {
                _lastSafePlayerPos = _localPlayer.GetPosition();
                _playerReady = true;
            }

            _lastViolated = false;
            _active = true;
        }

        /// <summary>
        /// Stops guarding and fully resets all runtime state. Safe to call before a subsequent
        /// Begin() when the instance is being reused without being recreated.
        /// </summary>
        public void End()
        {
            _active = false;
            _lastViolated = false;
            _playerReady = false;
            _count = 0;
            _batchStart = 0;
            _batchSize = 0;
            _candidateCount = 0;
            _lastHeadPos = Vector3.zero;
            _lastSafePlayerPos = Vector3.zero;
            _centers = null;
            _invRotations = null;
            _marginHalfExtents = null;
            _aabbMin = null;
            _aabbMax = null;
            _candidatePos = null;
            _candidateIndices = null;
        }

        #endregion

        #region Private Methods

        private void AllocateArrays(int n)
        {
            _count = n;
            if (_batchFrames < 1) _batchFrames = 1;
            _batchSize = n > 0 ? (n + _batchFrames - 1) / _batchFrames : 0;
            _batchStart = 0;

            _centers = new Vector3[n];
            _invRotations = new Quaternion[n];
            _marginHalfExtents = new Vector3[n];
            _aabbMin = new Vector3[n];
            _aabbMax = new Vector3[n];
            _candidatePos = new int[n];
            _candidateIndices = new int[n];
        }

        private void BakeCollider(BoxCollider col, int i)
        {
            Transform t = col.transform;
            Quaternion rot = t.rotation;
            Vector3 scale = t.lossyScale;

            Vector3 center = t.TransformPoint(col.center);
            float hx = col.size.x * Mathf.Abs(scale.x) * 0.5f;
            float hy = col.size.y * Mathf.Abs(scale.y) * 0.5f;
            float hz = col.size.z * Mathf.Abs(scale.z) * 0.5f;
            float m = _margin;

            _centers[i] = center;
            // t.rotation is always a unit quaternion; conjugate == inverse, no normalisation needed.
            _invRotations[i] = new Quaternion(-rot.x, -rot.y, -rot.z, rot.w);
            _marginHalfExtents[i] = new Vector3(hx + m, hy + m, hz + m);

            // Tight world-space AABB: for each world axis the half-extent is the sum of
            // the absolute projections of each OBB axis scaled by its half-extent.
            Vector3 ex = rot * new Vector3(hx, 0f, 0f);
            Vector3 ey = rot * new Vector3(0f, hy, 0f);
            Vector3 ez = rot * new Vector3(0f, 0f, hz);
            float wx = Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x) + m;
            float wy = Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y) + m;
            float wz = Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z) + m;
            _aabbMin[i] = new Vector3(center.x - wx, center.y - wy, center.z - wz);
            _aabbMax[i] = new Vector3(center.x + wx, center.y + wy, center.z + wz);
        }

        /// <summary>
        /// Populates the initial candidate list before the first PostLateUpdate.
        /// Uses capsule position + average eye height as a head proxy since tracking data
        /// is not reliable before the first PostLateUpdate.
        /// </summary>
        private void InitCandidates()
        {
            _candidateCount = 0;
            _batchStart = 0;
            for (int i = 0; i < _count; i++) _candidatePos[i] = -1;
            _lastHeadPos = Vector3.zero; // always initialise; corrected on first PostLateUpdate when player is valid

            if (_localPlayer == null || !_localPlayer.IsValid()) return;

            Vector3 approxHead = _localPlayer.GetPosition() + new Vector3(0f, 1.6f, 0f);
            _lastHeadPos = approxHead;

            // Batch AABB check: headPos.x + e >= aabbMin.x  AND  headPos.x - e <= aabbMax.x
            // Precomputed once outside the loop.
            float e = _batchScanExpansion;
            float hxP = approxHead.x + e, hxN = approxHead.x - e;
            float hyP = approxHead.y + e, hyN = approxHead.y - e;
            float hzP = approxHead.z + e, hzN = approxHead.z - e;

            Vector3[] aabbMin = _aabbMin;
            Vector3[] aabbMax = _aabbMax;
            int[] candidateIndices = _candidateIndices;
            int[] candidatePos = _candidatePos;
            int cand = 0;

            for (int i = 0; i < _count; i++)
            {
                Vector3 mn = aabbMin[i];
                Vector3 mx = aabbMax[i];
                if (hxP < mn.x || hxN > mx.x ||
                    hyP < mn.y || hyN > mx.y ||
                    hzP < mn.z || hzN > mx.z) continue;

                candidateIndices[cand] = i;
                candidatePos[i] = cand++;
            }

            _candidateCount = cand;
        }

        /// <summary>
        /// Advances the rolling batch scan by one slice, updating the candidate list.
        ///
        /// The batch AABB for OBB i is the stored tight AABB expanded by _batchScanExpansion:
        ///   near ↔ headPos + e >= aabbMin[i]  AND  headPos - e <= aabbMax[i]
        /// headPos ± e is precomputed once before the loop.
        ///
        /// Candidates are added (append) and removed (swap-with-last) in O(1).
        /// </summary>
        // Returns the post-scan candidate count so the caller can use it directly,
        // avoiding a field read for both the zero-check and the loop bound.
        private int AdvanceBatch(Vector3 headPos)
        {
            int count = _count;
            if (count == 0) return _candidateCount;

            float e = _batchScanExpansion;
            float hxP = headPos.x + e, hxN = headPos.x - e;
            float hyP = headPos.y + e, hyN = headPos.y - e;
            float hzP = headPos.z + e, hzN = headPos.z - e;

            // Cache _batchStart to avoid reading the field twice (end computation + loop init).
            int start = _batchStart;
            int end = start + _batchSize;
            if (end > count) end = count;

            // Cache array references and the scalar _candidateCount as locals to avoid
            // repeated field lookups and a final write-back for the scalar.
            Vector3[] aabbMin = _aabbMin;
            Vector3[] aabbMax = _aabbMax;
            int[] candidatePos = _candidatePos;
            int[] candidateIndices = _candidateIndices;
            int cand = _candidateCount;

            for (int i = start; i < end; i++)
            {
                Vector3 mn = aabbMin[i];
                Vector3 mx = aabbMax[i];
                bool near = hxP >= mn.x && hxN <= mx.x &&
                            hyP >= mn.y && hyN <= mx.y &&
                            hzP >= mn.z && hzN <= mx.z;

                int cp = candidatePos[i];
                if (near && cp < 0)
                {
                    candidateIndices[cand] = i;
                    candidatePos[i] = cand++;
                }
                else if (!near && cp >= 0)
                {
                    int last = candidateIndices[--cand];
                    // Guard the self-swap: when i is already the last element, last == i and
                    // candidatePos[last] must not be written before candidatePos[i] = -1.
                    if (last != i)
                    {
                        candidateIndices[cp] = last;
                        candidatePos[last] = cp;
                    }
                    candidatePos[i] = -1;
                }
            }

            _candidateCount = cand;
            _batchStart = (end >= count) ? 0 : end;
            return cand;
        }

        /// <summary>
        /// Returns the minimum OBB-local push to move headLocal outside the box defined by mHalf.
        ///
        /// The entry face is identified by the axis on which capsuleLocal most exceeds the raw
        /// half-extents (mHalf - _margin). The push target along that axis is mHalf.comp + 0.001.
        ///
        /// Degenerate path (capsule also inside the OBB): minimum-penetration-depth push toward
        /// the nearest face.
        /// </summary>
        private Vector3 ComputePushLocal(Vector3 headLocal, Vector3 capsuleLocal, Vector3 mHalf)
        {
            // Cache struct components to avoid repeated property reads.
            float mx = mHalf.x, my = mHalf.y, mz = mHalf.z;
            float m = _margin;
            float hx = mx - m, hy = my - m, hz = mz - m;

            float cx = capsuleLocal.x, cy = capsuleLocal.y, cz = capsuleLocal.z;
            float acx = cx < 0f ? -cx : cx;
            float acy = cy < 0f ? -cy : cy;
            float acz = cz < 0f ? -cz : cz;

            float exceedX = acx - hx;
            float exceedY = acy - hy;
            float exceedZ = acz - hz;

            Vector3 push = Vector3.zero;

            if (exceedX >= exceedY && exceedX >= exceedZ && exceedX > 0f)
            {
                push.x = (cx >= 0f ? 1f : -1f) * (mx + 0.001f) - headLocal.x;
            }
            else if (exceedY >= exceedZ && exceedY > 0f)
            {
                push.y = (cy >= 0f ? 1f : -1f) * (my + 0.001f) - headLocal.y;
            }
            else if (exceedZ > 0f)
            {
                push.z = (cz >= 0f ? 1f : -1f) * (mz + 0.001f) - headLocal.z;
            }
            else
            {
                // Degenerate: capsule is also inside the OBB. Push toward nearest face.
                float hlx = headLocal.x, hly = headLocal.y, hlz = headLocal.z;
                float ahx = hlx < 0f ? -hlx : hlx;
                float ahy = hly < 0f ? -hly : hly;
                float ahz = hlz < 0f ? -hlz : hlz;
                float px = mx - ahx;
                float py = my - ahy;
                float pz = mz - ahz;
                if (px <= py && px <= pz)
                    push.x = (hlx >= 0f ? 1f : -1f) * (px + 0.001f);
                else if (py <= pz)
                    push.y = (hly >= 0f ? 1f : -1f) * (py + 0.001f);
                else
                    push.z = (hlz >= 0f ? 1f : -1f) * (pz + 0.001f);
            }

            return push;
        }

        #endregion
    }
}
