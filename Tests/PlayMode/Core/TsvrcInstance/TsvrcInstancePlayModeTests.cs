using System.Collections;
using NUnit.Framework;
using Tsvrc.Core;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // Edit Mode (Tests/Editor/Core/TsvrcInstance/) covers everything reachable
    // without a real ClientSim session. This file adds the one thing only ClientSim
    // can prove: that against a genuine spawned local player, IsTsMaster reads
    // exactly what Networking.IsMaster reads, not a stale/cached value.
    //
    // NUnit Assert failures inside a [UnityTest] are silent in this project's Play
    // Mode environment: no exception, no stack trace, nothing in the log, and the
    // NUnit results XML is never written for a Play Mode run either. Every test here
    // therefore does its own manual pass/fail check and always logs exactly one
    // PLAYMODE_TEST_RESULT marker, never relying on Assert for the actual assertion.
    public class TsvrcInstancePlayModeTests
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
        public IEnumerator IsTsMaster_SoloLocalPlayerIsMaster_MatchesRealNetworkingIsMaster()
        {
            const string testName = "IsTsMaster_SoloLocalPlayerIsMaster_MatchesRealNetworkingIsMaster";

            yield return StartClientSim(localPlayerIsMaster: true);

            GameObject go = new GameObject("Instance_IsTsMaster_Master");
            TsvrcInstance instance = go.AddComponent<TsvrcInstance>();

            bool networkingIsMaster = Networking.IsMaster;
            bool isTsMaster = instance.IsTsMaster;

            Object.DestroyImmediate(go);

            bool passed = networkingIsMaster && isTsMaster && networkingIsMaster == isTsMaster;
            LogResult(testName, passed, "networkingIsMaster=" + networkingIsMaster + " isTsMaster=" + isTsMaster);
        }

        [UnityTest]
        public IEnumerator IsTsMaster_SoloLocalPlayerNotMaster_MatchesRealNetworkingIsMaster()
        {
            const string testName = "IsTsMaster_SoloLocalPlayerNotMaster_MatchesRealNetworkingIsMaster";

            yield return StartClientSim(localPlayerIsMaster: false);

            GameObject go = new GameObject("Instance_IsTsMaster_NotMaster");
            TsvrcInstance instance = go.AddComponent<TsvrcInstance>();

            bool networkingIsMaster = Networking.IsMaster;
            bool isTsMaster = instance.IsTsMaster;

            Object.DestroyImmediate(go);

            bool passed = !networkingIsMaster && !isTsMaster && networkingIsMaster == isTsMaster;
            LogResult(testName, passed, "networkingIsMaster=" + networkingIsMaster + " isTsMaster=" + isTsMaster);
        }
    }
}
