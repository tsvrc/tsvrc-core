using System.Collections;
using NUnit.Framework;
using Tsvrc.Utils;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // Edit Mode (Tests/Editor/Utils/TsMemory/) covers every branch of Register/Set/Add/Get*/
    // Remove/Clear/OnDeserialization without throwing, since none of those paths actually
    // require a real player or network session to execute. What's left for Play Mode is the
    // genuinely player/session-dependent surface: OnPlayerRestored with a real VRCPlayerApi,
    // a real PlayerData round trip, and confirming ownership-transfer calls don't disrupt a
    // solo session. Follows the ClientSim bootstrap and PLAYMODE_TEST_RESULT log-marker
    // conventions established in Tests/PlayMode/Core/TsvrcProcess/TsvrcProcessOwnershipHandoverTests.cs.
    public class TsMemoryPlayModeTests
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

        private static void LogResult(string testName, bool passed, string details) =>
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);

        private static TsMemory CreateMemory(string name)
        {
            var go = new GameObject(name);
            return go.AddComponent<TsMemory>();
        }

        [UnityTest]
        public IEnumerator OnPlayerRestored_RealLocalPlayer_SetsIsPlayerRestoredTrue()
        {
            const string testName = "OnPlayerRestored_RealLocalPlayer_SetsIsPlayerRestoredTrue";
            yield return StartClientSim();

            TsMemory memory = CreateMemory("Memory_Restored");
            bool beforeRestore = memory.IsPlayerRestored;

            memory.OnPlayerRestored(Networking.LocalPlayer);

            bool passed = !beforeRestore && memory.IsPlayerRestored;
            Object.DestroyImmediate(memory.gameObject);
            LogResult(testName, passed, "beforeRestore=" + beforeRestore + " afterRestore=" + memory.IsPlayerRestored);
        }

        [UnityTest]
        public IEnumerator OnPlayerRestored_RealRemotePlayer_DoesNotSetIsPlayerRestored()
        {
            const string testName = "OnPlayerRestored_RealRemotePlayer_DoesNotSetIsPlayerRestored";
            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RemoteRestore");
            yield return null;
            yield return null;
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            VRCPlayerApi remote = null;
            foreach (VRCPlayerApi p in players)
                if (p != null && p.displayName == "RemoteRestore") remote = p;
            Assert.IsNotNull(remote, "RemoteRestore was not spawned.");

            TsMemory memory = CreateMemory("Memory_RemoteRestore");

            memory.OnPlayerRestored(remote);

            bool passed = !memory.IsPlayerRestored;
            Object.DestroyImmediate(memory.gameObject);
            LogResult(testName, passed, "isPlayerRestored=" + memory.IsPlayerRestored);
        }

        [UnityTest]
        public IEnumerator Set_PersistKeyAfterRestore_RoundTripsThroughRealPlayerData()
        {
            const string testName = "Set_PersistKeyAfterRestore_RoundTripsThroughRealPlayerData";
            yield return StartClientSim();

            TsMemory memory = CreateMemory("Memory_PdRoundTrip");
            memory.Register("pdKey", true, false);
            memory.Add("pdKey", new DataToken("seed"));
            memory.OnPlayerRestored(Networking.LocalPlayer);

            memory.Set("pdKey", new DataToken("written"));
            yield return null;

            bool hasKey = PlayerData.HasKey(Networking.LocalPlayer, "pdKey");
            string pdValue = hasKey ? PlayerData.GetString(Networking.LocalPlayer, "pdKey") : "<missing>";
            string memValue = memory.GetString("pdKey");

            bool passed = hasKey && pdValue == "written" && memValue == "written";
            Object.DestroyImmediate(memory.gameObject);
            LogResult(testName, passed, "hasKey=" + hasKey + " pdValue=" + pdValue + " memValue=" + memValue);
        }

        [UnityTest]
        public IEnumerator Set_SyncedKey_DoesNotDisruptSoloSessionOwnership()
        {
            const string testName = "Set_SyncedKey_DoesNotDisruptSoloSessionOwnership";
            yield return StartClientSim();

            TsMemory memory = CreateMemory("Memory_Ownership");
            memory.Register("k", false, true);

            memory.Set("k", new DataToken("v"));
            yield return null;

            bool isOwner = Networking.IsOwner(memory.gameObject);
            bool hasValue = memory.Has("k") && memory.GetString("k") == "v";

            bool passed = isOwner && hasValue;
            Object.DestroyImmediate(memory.gameObject);
            LogResult(testName, passed, "isOwner=" + isOwner + " hasValue=" + hasValue);
        }

        [UnityTest]
        public IEnumerator OnDeserialization_AfterRealRequestSerialization_UpdatesSyncedStore()
        {
            const string testName = "OnDeserialization_AfterRealRequestSerialization_UpdatesSyncedStore";
            yield return StartClientSim();

            TsMemory memory = CreateMemory("Memory_Deserialize");
            memory.Register("k", false, true);
            memory.Set("k", new DataToken("v"));
            yield return null;
            yield return null;

            // The local owner does not receive its own OnDeserialization callback under
            // normal VRChat networking; call it directly to prove the synced store the
            // owner already holds locally matches what a remote OnDeserialization would
            // parse from the same _syncedJson field.
            memory.OnDeserialization();

            bool passed = memory.Has("k") && memory.GetString("k") == "v";
            Object.DestroyImmediate(memory.gameObject);
            LogResult(testName, passed, "has=" + memory.Has("k"));
        }
    }
}
