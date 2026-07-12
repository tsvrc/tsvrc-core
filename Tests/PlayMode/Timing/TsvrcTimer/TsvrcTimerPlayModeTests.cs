using System.Collections;
using NUnit.Framework;
using Tsvrc.Core;
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
    // Real ClientSim Play Mode tests for TsvrcTimer, following the exact harness/
    // methodology TsvrcProcessOwnershipHandoverTests.cs established for this project:
    // NUnit Assert.X failures are silent in Play Mode here (no exception, no stack
    // trace, results XML never written), so every test does its own pass/fail check
    // and logs exactly one PLAYMODE_TEST_RESULT marker, verified by reading the
    // -logFile Console output, not the Test Runner's pass/fail indicator.
    //
    // SendCustomEventDelayedSeconds/RequestSerialization are no-op stubs in both Edit
    // and Play Mode for a plain, uncompiled proxy script, and a plain proxy can never
    // organically receive VRChat callbacks like OnPlayerLeft/OnDeserialization either -
    // so ClientSim adds nothing for the tick loop or for OnDeserialization's own
    // event-inference logic (already fully covered by TsvrcTimerDeserializationTests.cs).
    // What it does add: a real Networking.LocalPlayer/GetServerTimeInMilliseconds()
    // and a real (if execution-inert) remote VRCPlayerApi, enough to verify two things
    // Edit Mode structurally cannot - that GetServerTimeMilliseconds() returns a real,
    // live, advancing clock value, and that StartTimer()'s real Networking.SetOwner/
    // IsOwner round-trip actually claims Unity-level ownership end-to-end with zero
    // reflection seeding.
    public class TsvrcTimerPlayModeTests
    {
        private GameObject _descriptorObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (ClientSimMain.HasInstance())
                ClientSimMain.RemoveInstance();

            ClientSimRuntimeLoader.EndUnityTesting();

            if (_descriptorObject != null)
                UnityEngine.Object.DestroyImmediate(_descriptorObject);

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

        private static VRCPlayerApi FindPlayerByName(string name)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            foreach (VRCPlayerApi p in players)
                if (p != null && p.displayName == name) return p;
            return null;
        }

        private static void LogResult(string testName, bool passed, string details)
        {
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);
        }

        [UnityTest]
        public IEnumerator GetServerTimeMilliseconds_RealClientSim_AdvancesWithRealWallClockTime()
        {
            const string testName = "GetServerTimeMilliseconds_RealClientSim_AdvancesWithRealWallClockTime";

            yield return StartClientSim();

            GameObject go = new GameObject("Timer_ServerClock");
            TsvrcTimerTestSubclass timer = go.AddComponent<TsvrcTimerTestSubclass>();
            timer.TsConstruct((TsvrcRoot)null);

            int first = timer.GetServerTimeMilliseconds();
            yield return new WaitForSecondsRealtime(1f);
            int second = timer.GetServerTimeMilliseconds();

            UnityEngine.Object.DestroyImmediate(go);

            int delta = second - first;
            // Generous tolerance for CI/editor scheduling jitter - this is checking
            // "is this a real, live, advancing clock" not measuring precise timing.
            bool passed = first >= 0 && second >= 0 && delta >= 500 && delta <= 5000;
            LogResult(testName, passed, "first=" + first + " second=" + second + " delta=" + delta);
        }

        [UnityTest]
        public IEnumerator StartTimer_RealClientSimLocalPlayer_ClaimsRealUnityOwnershipAndElapsedGrowsOverRealTime()
        {
            const string testName = "StartTimer_RealClientSimLocalPlayer_ClaimsRealUnityOwnershipAndElapsedGrowsOverRealTime";

            yield return StartClientSim();

            GameObject go = new GameObject("Timer_StartTimer");
            TsvrcTimerTestSubclass timer = go.AddComponent<TsvrcTimerTestSubclass>();
            timer.TsConstruct((TsvrcRoot)null);

            timer.StartTimer();
            yield return null;

            bool isUnityOwnerRightAfterStart = Networking.IsOwner(go);
            int startedCount = timer.OnTimerStartedCount;

            yield return new WaitForSecondsRealtime(1f);

            int elapsedAfterRealWait = timer.GetElapsedMilliseconds();

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = isUnityOwnerRightAfterStart && startedCount == 1 &&
                elapsedAfterRealWait >= 500 && elapsedAfterRealWait <= 5000;
            LogResult(testName, passed, "isUnityOwnerRightAfterStart=" + isUnityOwnerRightAfterStart +
                " startedCount=" + startedCount + " elapsedAfterRealWait=" + elapsedAfterRealWait);
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RealDepartedOwner_TakeOverPreservesElapsedMathForNewOwner()
        {
            const string testName = "OnPlayerLeft_RealDepartedOwner_TakeOverPreservesElapsedMathForNewOwner";

            yield return StartClientSim();

            GameObject go = new GameObject("Timer_OnPlayerLeft");
            TsvrcTimerTestSubclass timer = go.AddComponent<TsvrcTimerTestSubclass>();
            timer.TsConstruct((TsvrcRoot)null);

            const string remoteName = "OwnerWhoLeaves";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");

            // Simulate an in-progress run "owned" by the remote, anchored to the real
            // server clock (1.5s already elapsed) rather than a synthetic value, so the
            // elapsed-math-survives-handover assertion below is checked against a real
            // GetServerTimeMilliseconds() delta, not a canned number.
            int nowMs = timer.GetServerTimeMilliseconds();
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            PrivateFieldAccess.SetField(timer, "_startServerTimeMs", nowMs - 1500);
            PrivateFieldAccess.SetField(timer, "_durationMs", 0);
            PrivateFieldAccess.SetField(timer, "_ownerId", TsPlayer.GetPlayerID(remote));
            PrivateFieldAccess.SetField(timer, "_ownerPlayerIdInt", remote.playerId);

            int elapsedBeforeHandover = timer.GetElapsedMilliseconds();

            // Same structural reason as TsvrcProcessOwnershipHandoverTests: ClientSim's
            // OnPlayerLeft broadcast only reaches compiled UdonBehaviour VM instances,
            // never a plain proxy script - direct invocation with the real departed
            // VRCPlayerApi is the established substitute.
            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            timer.OnPlayerLeft(remote);

            int elapsedAfterHandover = timer.GetElapsedMilliseconds();
            bool isUnityOwnerNow = Networking.IsOwner(go);
            int localIntId = Networking.LocalPlayer.playerId;
            int ownerIntIdAfter = PrivateFieldAccess.GetField<int>(timer, "_ownerPlayerIdInt");

            UnityEngine.Object.DestroyImmediate(go);

            int elapsedDelta = Mathf.Abs(elapsedAfterHandover - elapsedBeforeHandover);
            bool passed = ownerIntIdAfter == localIntId && isUnityOwnerNow && elapsedDelta < 1000;
            LogResult(testName, passed, "ownerIntIdAfter=" + ownerIntIdAfter + " localIntId=" + localIntId +
                " isUnityOwnerNow=" + isUnityOwnerNow + " elapsedBeforeHandover=" + elapsedBeforeHandover +
                " elapsedAfterHandover=" + elapsedAfterHandover);
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RealDepartedOwnerWhilePaused_TakeOverPreservesPausedStateAndFrozenElapsed()
        {
            // Companion to the mid-run handover test above, covering the mid-PAUSE
            // case: owner leaving while the timer is paused must preserve _isPaused
            // and the frozen _elapsedOffsetMs for the new owner. While paused,
            // GetElapsedMilliseconds() reads the frozen _elapsedOffsetMs directly
            // rather than computing from _startServerTimeMs against the live clock,
            // so this also proves the handover doesn't accidentally "unfreeze" time
            // for the new owner.
            const string testName = "OnPlayerLeft_RealDepartedOwnerWhilePaused_TakeOverPreservesPausedStateAndFrozenElapsed";

            yield return StartClientSim();

            GameObject go = new GameObject("Timer_OnPlayerLeft_Paused");
            TsvrcTimerTestSubclass timer = go.AddComponent<TsvrcTimerTestSubclass>();
            timer.TsConstruct((TsvrcRoot)null);

            const string remoteName = "PausedOwnerWhoLeaves";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");

            // Simulate a run "owned" by the remote, paused with 1500ms frozen elapsed.
            // _startServerTimeMs is deliberately left far in the past (rather than
            // anchored to "now" like the mid-run test) - while paused it must be
            // completely ignored by GetElapsedMilliseconds(), so any leakage of real
            // clock time into the result would show up as elapsedAfterHandover
            // drifting away from the frozen 1500.
            int nowMs = timer.GetServerTimeMilliseconds();
            PrivateFieldAccess.SetField(timer, "_isRunning", true);
            PrivateFieldAccess.SetField(timer, "_isPaused", true);
            PrivateFieldAccess.SetField(timer, "_startServerTimeMs", nowMs - 999999);
            PrivateFieldAccess.SetField(timer, "_elapsedOffsetMs", 1500);
            PrivateFieldAccess.SetField(timer, "_durationMs", 0);
            PrivateFieldAccess.SetField(timer, "_ownerId", TsPlayer.GetPlayerID(remote));
            PrivateFieldAccess.SetField(timer, "_ownerPlayerIdInt", remote.playerId);

            int elapsedBeforeHandover = timer.GetElapsedMilliseconds();

            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            timer.OnPlayerLeft(remote);
            yield return new WaitForSecondsRealtime(0.5f);

            int elapsedAfterHandover = timer.GetElapsedMilliseconds();
            bool stillPaused = PrivateFieldAccess.GetField<bool>(timer, "_isPaused");
            bool isUnityOwnerNow = Networking.IsOwner(go);
            int localIntId = Networking.LocalPlayer.playerId;
            int ownerIntIdAfter = PrivateFieldAccess.GetField<int>(timer, "_ownerPlayerIdInt");

            UnityEngine.Object.DestroyImmediate(go);

            bool passed = ownerIntIdAfter == localIntId && isUnityOwnerNow && stillPaused &&
                elapsedBeforeHandover == 1500 && elapsedAfterHandover == 1500;
            LogResult(testName, passed, "ownerIntIdAfter=" + ownerIntIdAfter + " localIntId=" + localIntId +
                " isUnityOwnerNow=" + isUnityOwnerNow + " stillPaused=" + stillPaused +
                " elapsedBeforeHandover=" + elapsedBeforeHandover + " elapsedAfterHandover=" + elapsedAfterHandover);
        }
    }
}
