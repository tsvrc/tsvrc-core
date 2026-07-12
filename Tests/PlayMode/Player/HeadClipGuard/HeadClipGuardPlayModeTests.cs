using System.Collections;
using NUnit.Framework;
using Tsvrc.Core.Generated;
using Tsvrc.Player;
using Tsvrc.Tests.Editor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // Every branch reachable without a real VRCPlayerApi already has Edit Mode
    // coverage (Tests/Editor/Player/HeadClipGuard/). This file adds only what
    // needs a genuine spawned local player: the real IsValid()/GetPosition()/
    // GetTrackingData() paths in InitCandidates/Begin/PostLateUpdate.
    //
    // NUnit Assert failures inside a [UnityTest] are silent in this project's Play
    // Mode environment - every test here does its own manual pass/fail check and
    // logs a PLAYMODE_TEST_RESULT marker rather than relying on Assert for the
    // actual assertion. PostLateUpdate is called directly rather than relying on
    // it firing automatically: VRChat's own callback dispatch only reaches
    // compiled UdonBehaviour VM instances, not a plain, uncompiled proxy script.
    public class HeadClipGuardPlayModeTests
    {
        private GameObject _descriptorObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (ClientSimMain.HasInstance())
                ClientSimMain.RemoveInstance();

            ClientSimRuntimeLoader.EndUnityTesting();

            if (_descriptorObject != null)
                Object.DestroyImmediate(_descriptorObject);

            yield return null;
        }

        private void CreateMinimalSceneDescriptor()
        {
            _descriptorObject = new GameObject("__TestSceneDescriptor");
            VRCSceneDescriptor descriptor = _descriptorObject.AddComponent<VRCSceneDescriptor>();

            GameObject spawnObject = new GameObject("__TestSpawn");
            spawnObject.transform.SetParent(_descriptorObject.transform);

            descriptor.spawns = new[] { spawnObject.transform };
        }

        private IEnumerator StartClientSim()
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool("com.vrchat.clientsim.session.accepted_warning", true);
#endif
            CreateMinimalSceneDescriptor();

            ClientSimSettings settings = new ClientSimSettings
            {
                enableClientSim = true,
                spawnPlayer = true,
                deleteEditorOnly = false,
                localPlayerIsMaster = true,
                initializationDelay = 0f,
            };

            ClientSimRuntimeLoader.BeginUnityTesting(settings);
            ClientSimRuntimeLoader.StartClientSim(settings);

            yield return null;
            yield return null;
        }

        private static HeadClipGuard CreateConstructedGuard()
        {
            var go = new GameObject("HeadClipGuard");
            HeadClipGuard guard = go.AddComponent<HeadClipGuard>();
            guard.TsConstruct((TsvrcRoot)null); // runs TsStart(), caching _localPlayer
            return guard;
        }

        private static BoxCollider CreateEnclosingCollider(string name, Vector3 center, float halfExtent)
        {
            var go = new GameObject(name);
            go.transform.position = center;
            BoxCollider col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(halfExtent * 2f, halfExtent * 2f, halfExtent * 2f);
            return col;
        }

        private static void LogResult(string testName, bool passed, string details)
        {
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);
        }

        [UnityTest]
        public IEnumerator InitCandidates_RealValidPlayer_SeedsCandidateFromApproxHeadPosition()
        {
            const string testName = "InitCandidates_RealValidPlayer_SeedsCandidateFromApproxHeadPosition";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 approxHead = player.GetPosition() + new Vector3(0f, 1.6f, 0f);

            BoxCollider near = CreateEnclosingCollider("Near", approxHead, 1f);
            BoxCollider far = CreateEnclosingCollider("Far", approxHead + new Vector3(1000f, 0f, 0f), 1f);

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(new[] { near, far }, 2);

            int candidateCount = PrivateFieldAccess.GetField<int>(guard, "_candidateCount");
            int[] candidateIndices = PrivateFieldAccess.GetField<int[]>(guard, "_candidateIndices");
            bool nearIsCandidate = false;
            bool farIsCandidate = false;
            for (int i = 0; i < candidateCount; i++)
            {
                if (candidateIndices[i] == 0) nearIsCandidate = true;
                if (candidateIndices[i] == 1) farIsCandidate = true;
            }

            bool passed = nearIsCandidate && !farIsCandidate;
            LogResult(testName, passed, "candidateCount=" + candidateCount + " nearIsCandidate=" + nearIsCandidate + " farIsCandidate=" + farIsCandidate);
        }

        [UnityTest]
        public IEnumerator Begin_RealValidPlayer_PlayerReadyImmediately_LastSafePlayerPosSeeded()
        {
            const string testName = "Begin_RealValidPlayer_PlayerReadyImmediately_LastSafePlayerPosSeeded";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 expectedPos = player.GetPosition();

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(null, 0);

            bool playerReady = PrivateFieldAccess.GetField<bool>(guard, "_playerReady");
            Vector3 lastSafe = PrivateFieldAccess.GetField<Vector3>(guard, "_lastSafePlayerPos");

            bool passed = playerReady && Vector3.Distance(expectedPos, lastSafe) < 0.01f;
            LogResult(testName, passed, "playerReady=" + playerReady + " expectedPos=" + expectedPos + " lastSafe=" + lastSafe);
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_NoCollidersNearby_StationaryPlayer_NeverTeleports()
        {
            const string testName = "PostLateUpdate_NoCollidersNearby_StationaryPlayer_NeverTeleports";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 posBefore = player.GetPosition();

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(null, 0);

            guard.PostLateUpdate();
            guard.PostLateUpdate();
            guard.PostLateUpdate();

            Vector3 posAfter = player.GetPosition();
            bool lastViolated = PrivateFieldAccess.GetField<bool>(guard, "_lastViolated");

            bool passed = !lastViolated && Vector3.Distance(posBefore, posAfter) < 0.01f;
            LogResult(testName, passed, "posBefore=" + posBefore + " posAfter=" + posAfter + " lastViolated=" + lastViolated);
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_HeadInsideObbAfterBegin_TeleportsOutOnNextCall()
        {
            const string testName = "PostLateUpdate_HeadInsideObbAfterBegin_TeleportsOutOnNextCall";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 headPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 capsuleBefore = player.GetPosition();

            BoxCollider wall = CreateEnclosingCollider("Wall", headPos, 2f);

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(new[] { wall }, 1);
            // Force the movement gate open regardless of how close InitCandidates'
            // approximate head position happened to land relative to the real one.
            PrivateFieldAccess.SetField(guard, "_lastHeadPos", headPos + new Vector3(999f, 0f, 0f));

            guard.PostLateUpdate();
            yield return null;

            Vector3 capsuleAfter = player.GetPosition();
            bool lastViolated = PrivateFieldAccess.GetField<bool>(guard, "_lastViolated");

            bool passed = lastViolated && Vector3.Distance(capsuleBefore, capsuleAfter) > 0.01f;
            LogResult(testName, passed, "lastViolated=" + lastViolated + " capsuleBefore=" + capsuleBefore + " capsuleAfter=" + capsuleAfter);
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_TwoOverlappingViolatedObbs_BothProcessedInOneFrame()
        {
            const string testName = "PostLateUpdate_TwoOverlappingViolatedObbs_BothProcessedInOneFrame";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 headPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            BoxCollider wallA = CreateEnclosingCollider("WallA", headPos, 2f);
            BoxCollider wallB = CreateEnclosingCollider("WallB", headPos + new Vector3(0.1f, 0f, 0f), 2f);

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(new[] { wallA, wallB }, 2);
            PrivateFieldAccess.SetField(guard, "_lastHeadPos", headPos + new Vector3(999f, 0f, 0f));

            guard.PostLateUpdate();

            bool lastViolated = PrivateFieldAccess.GetField<bool>(guard, "_lastViolated");
            int candidateCount = PrivateFieldAccess.GetField<int>(guard, "_candidateCount");

            bool passed = lastViolated && candidateCount == 2;
            LogResult(testName, passed, "lastViolated=" + lastViolated + " candidateCount=" + candidateCount);
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_PlayerReadyLatchesTrue_SeedsLastSafePlayerPosFromRealPosition()
        {
            const string testName = "PostLateUpdate_PlayerReadyLatchesTrue_SeedsLastSafePlayerPosFromRealPosition";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 expectedPos = player.GetPosition();

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(null, 0);
            // Simulate Begin() having run while the player was transiently invalid:
            // _playerReady never latched, _lastSafePlayerPos never got seeded.
            PrivateFieldAccess.SetField(guard, "_playerReady", false);
            PrivateFieldAccess.SetField(guard, "_lastSafePlayerPos", Vector3.zero);
            PrivateFieldAccess.SetField(guard, "_active", true);

            guard.PostLateUpdate();

            Vector3 lastSafe = PrivateFieldAccess.GetField<Vector3>(guard, "_lastSafePlayerPos");
            bool playerReady = PrivateFieldAccess.GetField<bool>(guard, "_playerReady");

            bool passed = playerReady && Vector3.Distance(expectedPos, lastSafe) < 0.01f;
            LogResult(testName, passed, "playerReady=" + playerReady + " expectedPos=" + expectedPos + " lastSafe=" + lastSafe);
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_LastViolatedBypassesMovementGate_ReprocessesDespiteZeroDelta()
        {
            const string testName = "PostLateUpdate_LastViolatedBypassesMovementGate_ReprocessesDespiteZeroDelta";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 headPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            BoxCollider wall = CreateEnclosingCollider("Wall", headPos, 2f);

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(new[] { wall }, 1);
            PrivateFieldAccess.SetField(guard, "_lastHeadPos", headPos + new Vector3(999f, 0f, 0f));

            guard.PostLateUpdate();
            yield return null;

            bool violatedAfterFirst = PrivateFieldAccess.GetField<bool>(guard, "_lastViolated");

            // Move the baked AABB far away (baked data is snapshotted at Begin() -
            // moving the real collider's Transform after Begin() has no effect) so
            // the second call finds nothing, and pin the current head position as
            // the comparison baseline so the movement gate WOULD skip if _lastViolated
            // did not bypass it.
            Vector3[] aabbMin = PrivateFieldAccess.GetField<Vector3[]>(guard, "_aabbMin");
            Vector3[] aabbMax = PrivateFieldAccess.GetField<Vector3[]>(guard, "_aabbMax");
            aabbMin[0] = new Vector3(9999f, 9999f, 9999f);
            aabbMax[0] = new Vector3(10000f, 10000f, 10000f);
            Vector3 headPosNow = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            PrivateFieldAccess.SetField(guard, "_lastHeadPos", headPosNow);

            guard.PostLateUpdate();

            bool violatedAfterSecond = PrivateFieldAccess.GetField<bool>(guard, "_lastViolated");

            bool passed = violatedAfterFirst && !violatedAfterSecond;
            LogResult(testName, passed, "violatedAfterFirst=" + violatedAfterFirst + " violatedAfterSecond=" + violatedAfterSecond);
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_MovementExactlyAtSkipThreshold_ProcessesFrame()
        {
            const string testName = "PostLateUpdate_MovementExactlyAtSkipThreshold_ProcessesFrame";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 headPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(null, 0);
            float movSkip = PrivateFieldAccess.GetField<float>(guard, "_movSkip");
            // dx == _movSkip exactly: the gate's strict "dx < skip" must be false at
            // equality, so this frame is not within the skip zone and gets processed.
            PrivateFieldAccess.SetField(guard, "_lastHeadPos", headPos - new Vector3(movSkip, 0f, 0f));
            PrivateFieldAccess.SetField(guard, "_lastViolated", false);

            guard.PostLateUpdate();

            Vector3 lastHeadPosAfter = PrivateFieldAccess.GetField<Vector3>(guard, "_lastHeadPos");
            bool passed = Vector3.Distance(lastHeadPosAfter, headPos) < 0.0001f;
            LogResult(testName, passed, "movSkip=" + movSkip + " headPos=" + headPos + " lastHeadPosAfter=" + lastHeadPosAfter);
        }

        [UnityTest]
        public IEnumerator PostLateUpdate_MovementBelowSkipThreshold_SkipsFrame()
        {
            const string testName = "PostLateUpdate_MovementBelowSkipThreshold_SkipsFrame";
            yield return StartClientSim();

            VRCPlayerApi player = Networking.LocalPlayer;
            Vector3 headPos = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;

            HeadClipGuard guard = CreateConstructedGuard();
            guard.Begin(null, 0);
            float movSkip = PrivateFieldAccess.GetField<float>(guard, "_movSkip");
            Vector3 staleHeadPos = headPos - new Vector3(movSkip * 0.5f, 0f, 0f);
            PrivateFieldAccess.SetField(guard, "_lastHeadPos", staleHeadPos);
            PrivateFieldAccess.SetField(guard, "_lastViolated", false);

            guard.PostLateUpdate();

            Vector3 lastHeadPosAfter = PrivateFieldAccess.GetField<Vector3>(guard, "_lastHeadPos");
            bool passed = Vector3.Distance(lastHeadPosAfter, staleHeadPos) < 0.0001f;
            LogResult(testName, passed, "movSkip=" + movSkip + " staleHeadPos=" + staleHeadPos + " lastHeadPosAfter=" + lastHeadPosAfter);
        }
    }
}
