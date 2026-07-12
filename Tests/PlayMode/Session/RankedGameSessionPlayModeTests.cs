using System.Collections;
using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.Session;
using Tsvrc.Tests.Editor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;

namespace Tsvrc.Tests.PlayMode
{
    // Real ClientSim Play Mode test for RankedGameSession's master gate, following the
    // established harness/methodology (TsvrcProcessOwnershipHandoverTests.cs,
    // TsvrcTimerPlayModeTests.cs): NUnit Assert.X failures are silent in Play Mode here,
    // so this logs exactly one PLAYMODE_TEST_RESULT marker, verified via the -logFile
    // Console output, not the Test Runner's pass/fail indicator.
    //
    // The Edit Mode suite already confirmed a real TsvrcInstance's IsTsMaster defaults
    // to true with no networking session established at all, which only reaches the
    // master-only ALLOWED branch. The DENIED branch needs a real local player that is
    // genuinely not master, which ClientSimSettings.localPlayerIsMaster=false provides
    // for real (unlike anything constructible in Edit Mode).
    public class RankedGameSessionPlayModeTests
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

        private IEnumerator StartClientSim(bool localPlayerIsMaster)
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
                localPlayerIsMaster = localPlayerIsMaster,
                initializationDelay = 0f,
            };

            ClientSimRuntimeLoader.BeginUnityTesting(settings);
            ClientSimRuntimeLoader.StartClientSim(settings);

            yield return null;
            yield return null;
        }

        private static void LogResult(string testName, bool passed, string details)
        {
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);
        }

        [UnityTest]
        public IEnumerator StartSession_MasterOnlyTrueRealNonMasterLocalPlayer_WarnsAndDoesNotStart()
        {
            const string testName = "StartSession_MasterOnlyTrueRealNonMasterLocalPlayer_WarnsAndDoesNotStart";

            yield return StartClientSim(localPlayerIsMaster: false);

            var session = new GameObject("RGS_NotMaster").AddComponent<RankedGameSessionTestSubclass>();
            var lobbyTracker = new GameObject("Lobby").AddComponent<PlayerTrackerTestSubclass>();
            var readyCheck = new GameObject("ReadyCheck").AddComponent<ReadyCheckProcessTestSubclass>();
            var gameTracker = new GameObject("Game").AddComponent<PlayerTrackerTestSubclass>();
            var completedTracker = new GameObject("Completed").AddComponent<PlayerTrackerTestSubclass>();
            var timer = new GameObject("Timer").AddComponent<TsvrcTimerTestSubclass>();

            PrivateFieldAccess.SetField(session, "_lobbyTracker", lobbyTracker);
            PrivateFieldAccess.SetField(session, "_readyCheck", readyCheck);
            PrivateFieldAccess.SetField(session, "_gameTracker", gameTracker);
            PrivateFieldAccess.SetField(session, "_completedTracker", completedTracker);
            PrivateFieldAccess.SetField(session, "_timer", timer);

            var instance = new GameObject("Instance").AddComponent<TsvrcInstance>();
            var root = new InstanceOnlyTsvrcRootDouble { FakeInstance = instance };

            session.TsConstruct(root);
            bool isMasterObserved = instance.IsTsMaster;

            session.StartLobbyTracking();
            lobbyTracker.AddTrackedPlayers(new[] { "A" });

            session.StartSession();
            yield return null;

            int currentState = session.CurrentState;

            UnityEngine.Object.DestroyImmediate(session.gameObject);
            UnityEngine.Object.DestroyImmediate(lobbyTracker.gameObject);
            UnityEngine.Object.DestroyImmediate(readyCheck.gameObject);
            UnityEngine.Object.DestroyImmediate(gameTracker.gameObject);
            UnityEngine.Object.DestroyImmediate(completedTracker.gameObject);
            UnityEngine.Object.DestroyImmediate(timer.gameObject);
            UnityEngine.Object.DestroyImmediate(instance.gameObject);

            bool passed = !isMasterObserved && currentState == RankedGameSessionState.Idle;
            LogResult(testName, passed, "isMasterObserved=" + isMasterObserved + " currentState=" + currentState);
        }
    }
}
