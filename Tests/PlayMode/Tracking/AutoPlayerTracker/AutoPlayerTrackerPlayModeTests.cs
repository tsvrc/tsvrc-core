using System.Collections;
using NUnit.Framework;
using Tsvrc.Core.Generated;
using Tsvrc.Player;
using Tsvrc.Tests.Editor;
using Tsvrc.Tracking;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // Real ClientSim Play Mode tests for the one part of AutoPlayerTracker that fundamentally
    // needs a real VRCPlayerApi list: everything that calls TsPlayer.GetAllPlayerIDs()
    // (StartAutoTracking, the StartPlayerTracking override's fresh-start branch) and
    // OnPlayerJoined itself. Edit Mode (Tests/Editor/Tracking/AutoPlayerTracker/) covers
    // everything else - VRCPlayerApi does not even resolve in that assembly. Follows the exact
    // harness/methodology established by PlayerTrackerAbandonmentTests.cs: NUnit Assert
    // failures are silent in this project's Play Mode environment, so every test computes its
    // own bool and logs exactly one PLAYMODE_TEST_RESULT marker. ClientSim never organically
    // delivers a networked call to a plain, uncompiled proxy script, so OnPlayerJoined is
    // invoked directly with a real VRCPlayerApi rather than relying on ClientSim's own dispatch.
    public class AutoPlayerTrackerPlayModeTests
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

        private static VRCPlayerApi FindPlayerByName(string name)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            foreach (VRCPlayerApi p in players)
                if (p != null && p.displayName == name) return p;
            return null;
        }

        private static bool Contains(string[] array, string value)
        {
            foreach (string s in array)
                if (s == value) return true;
            return false;
        }

        private static void LogResult(string testName, bool passed, string details)
        {
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);
        }

        [UnityTest]
        public IEnumerator StartAutoTracking_SoloLocalPlayer_TracksExactlyTheLocalPlayer()
        {
            const string testName = "StartAutoTracking_SoloLocalPlayer_TracksExactlyTheLocalPlayer";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_Solo");
            AutoPlayerTracker tracker = go.AddComponent<AutoPlayerTracker>();
            tracker.TsConstruct((TsvrcRoot)null);

            tracker.StartAutoTracking();

            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            string[] tracked = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");

            Object.DestroyImmediate(go);

            bool passed = tracked.Length == 1 && tracked[0] == localId;
            LogResult(testName, passed, "trackedLength=" + tracked.Length + " localId=" + localId);
        }

        [UnityTest]
        public IEnumerator StartAutoTracking_RemotePlayersAlreadyPresent_SnapshotsLocalAndAllRemotes()
        {
            const string testName = "StartAutoTracking_RemotePlayersAlreadyPresent_SnapshotsLocalAndAllRemotes";

            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("AlreadyHereA");
            ClientSimMain.SpawnRemotePlayer("AlreadyHereB");
            yield return null;
            yield return null;

            VRCPlayerApi remoteA = FindPlayerByName("AlreadyHereA");
            VRCPlayerApi remoteB = FindPlayerByName("AlreadyHereB");
            Assert.IsNotNull(remoteA, "AlreadyHereA was not spawned.");
            Assert.IsNotNull(remoteB, "AlreadyHereB was not spawned.");
            string remoteAId = TsPlayer.GetPlayerID(remoteA);
            string remoteBId = TsPlayer.GetPlayerID(remoteB);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            GameObject go = new GameObject("Tracker_MultiPresent");
            AutoPlayerTracker tracker = go.AddComponent<AutoPlayerTracker>();
            tracker.TsConstruct((TsvrcRoot)null);

            tracker.StartAutoTracking();

            string[] tracked = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");

            Object.DestroyImmediate(go);

            bool passed = tracked.Length == 3 && Contains(tracked, localId) &&
                Contains(tracked, remoteAId) && Contains(tracked, remoteBId);
            LogResult(testName, passed, "trackedLength=" + tracked.Length);
        }

        [UnityTest]
        public IEnumerator StartPlayerTracking_CustomArrayAndUseProcessUpdate_BothIgnoredInFavorOfRealSnapshot()
        {
            const string testName = "StartPlayerTracking_CustomArrayAndUseProcessUpdate_BothIgnoredInFavorOfRealSnapshot";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_IgnoredParams");
            AutoPlayerTracker tracker = go.AddComponent<AutoPlayerTracker>();
            tracker.TsConstruct((TsvrcRoot)null);

            tracker.StartPlayerTracking(new[] { "NotARealPlayer" }, true);

            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            string[] tracked = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            bool useProcessUpdate = PrivateFieldAccess.GetField<bool>(tracker, "_useProcessUpdate");

            Object.DestroyImmediate(go);

            bool passed = tracked.Length == 1 && tracked[0] == localId && !Contains(tracked, "NotARealPlayer") && !useProcessUpdate;
            LogResult(testName, passed, "trackedLength=" + tracked.Length + " useProcessUpdate=" + useProcessUpdate);
        }

        [UnityTest]
        public IEnumerator OnPlayerJoined_Owner_AddsNewlyJoinedRealPlayer()
        {
            const string testName = "OnPlayerJoined_Owner_AddsNewlyJoinedRealPlayer";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_OnPlayerJoined_Owner");
            AutoPlayerTracker tracker = go.AddComponent<AutoPlayerTracker>();
            tracker.TsConstruct((TsvrcRoot)null);
            tracker.StartAutoTracking();

            ClientSimMain.SpawnRemotePlayer("JoinsAfterStart");
            yield return null;
            yield return null;
            VRCPlayerApi joiner = FindPlayerByName("JoinsAfterStart");
            Assert.IsNotNull(joiner, "JoinsAfterStart was not spawned.");
            string joinerId = TsPlayer.GetPlayerID(joiner);

            tracker.OnPlayerJoined(joiner);

            string[] tracked = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");
            bool joinerTracked = Contains(tracked, joinerId);
            bool joinerInLastAdded = Contains(tracker.LastAddedPlayerIds, joinerId);

            Object.DestroyImmediate(go);

            bool passed = joinerTracked && joinerInLastAdded;
            LogResult(testName, passed, "trackedLength=" + tracked.Length + " joinerTracked=" + joinerTracked +
                " joinerInLastAdded=" + joinerInLastAdded);
        }

        [UnityTest]
        public IEnumerator OnPlayerJoined_NonOwner_IsNoOp()
        {
            const string testName = "OnPlayerJoined_NonOwner_IsNoOp";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_OnPlayerJoined_NonOwner");
            AutoPlayerTracker tracker = go.AddComponent<AutoPlayerTracker>();
            tracker.TsConstruct((TsvrcRoot)null);

            // Running, but owned by someone else - the local client is not the owner.
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            PrivateFieldAccess.SetField(tracker, "_ownerId", "SomeoneElse#999999");
            PrivateFieldAccess.SetField(tracker, "_ownerPlayerIdInt", 999999);
            PrivateFieldAccess.SetField(tracker, "_trackedPlayerIds", new string[0]);

            ClientSimMain.SpawnRemotePlayer("JoinsWhileNotOwner");
            yield return null;
            yield return null;
            VRCPlayerApi joiner = FindPlayerByName("JoinsWhileNotOwner");
            Assert.IsNotNull(joiner, "JoinsWhileNotOwner was not spawned.");

            bool threw = false;
            try
            {
                tracker.OnPlayerJoined(joiner);
            }
            catch
            {
                threw = true;
            }

            string[] tracked = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");

            Object.DestroyImmediate(go);

            bool passed = !threw && tracked.Length == 0;
            LogResult(testName, passed, "threw=" + threw + " trackedLength=" + tracked.Length);
        }

        [UnityTest]
        public IEnumerator OnPlayerJoined_ProcessNotRunning_IsNoOp()
        {
            const string testName = "OnPlayerJoined_ProcessNotRunning_IsNoOp";

            yield return StartClientSim();

            GameObject go = new GameObject("Tracker_OnPlayerJoined_NotRunning");
            AutoPlayerTracker tracker = go.AddComponent<AutoPlayerTracker>();
            tracker.TsConstruct((TsvrcRoot)null);
            // _isRunning left at its default false.

            ClientSimMain.SpawnRemotePlayer("JoinsWhileNotRunning");
            yield return null;
            yield return null;
            VRCPlayerApi joiner = FindPlayerByName("JoinsWhileNotRunning");
            Assert.IsNotNull(joiner, "JoinsWhileNotRunning was not spawned.");

            bool threw = false;
            try
            {
                tracker.OnPlayerJoined(joiner);
            }
            catch
            {
                threw = true;
            }

            string[] tracked = PrivateFieldAccess.GetField<string[]>(tracker, "_trackedPlayerIds");

            Object.DestroyImmediate(go);

            bool passed = !threw && tracked.Length == 0;
            LogResult(testName, passed, "threw=" + threw + " trackedLength=" + tracked.Length);
        }
    }
}
