using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // TsPlayer takes/returns VRCPlayerApi in every member except ToArray, and
    // VRCSDKBase.dll (where VRCPlayerApi is defined) has its Editor platform
    // support disabled in its own PluginImporter settings, so VRCPlayerApi is not
    // a usable type in an Editor-only assembly — that's why even the null/empty-
    // input cases below live here instead of in an Edit Mode suite.
    //
    // Assert failures inside a test method are not surfaced by this project's
    // Play Mode test runner, so every test computes its own bool and logs a
    // PLAYMODE_TEST_RESULT: <name> PASS/FAIL <observed values> line rather than
    // relying on Assert for the scenario-under-test check. Assert.IsNotNull is
    // still used for early "did ClientSim even spawn this player" sanity checks.
    public class TsPlayerPlayModeTests
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

        [Test]
        public void GetPlayerID_NullPlayer_ThrowsNullReferenceException()
        {
            const string testName = "GetPlayerID_NullPlayer_ThrowsNullReferenceException";
            bool threw = false;
            try { TsPlayer.GetPlayerID(null); }
            catch (NullReferenceException) { threw = true; }

            LogResult(testName, threw, "threw=" + threw);
            Assert.IsTrue(threw);
        }

        [Test]
        public void ToPlayerIDs_EmptyArray_ReturnsEmptyArray()
        {
            const string testName = "ToPlayerIDs_EmptyArray_ReturnsEmptyArray";
            string[] result = TsPlayer.ToPlayerIDs(Array.Empty<VRCPlayerApi>());

            bool passed = result.Length == 0;
            LogResult(testName, passed, "length=" + result.Length);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToPlayerIDs_Null_ThrowsNullReferenceException()
        {
            const string testName = "ToPlayerIDs_Null_ThrowsNullReferenceException";
            bool threw = false;
            try { TsPlayer.ToPlayerIDs(null); }
            catch (NullReferenceException) { threw = true; }

            LogResult(testName, threw, "threw=" + threw);
            Assert.IsTrue(threw);
        }

        [Test]
        public void ToPlayerApis_EmptyArray_ReturnsEmptyArrayWithoutTouchingLivePlayerList()
        {
            // ToPlayerApis returns before calling GetAllPlayers() when given an
            // empty array, so this never touches the live player list.
            const string testName = "ToPlayerApis_EmptyArray_ReturnsEmptyArrayWithoutTouchingLivePlayerList";
            VRCPlayerApi[] result = TsPlayer.ToPlayerApis(Array.Empty<string>());

            bool passed = result.Length == 0;
            LogResult(testName, passed, "length=" + result.Length);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void ToPlayerApis_Null_ThrowsNullReferenceException()
        {
            const string testName = "ToPlayerApis_Null_ThrowsNullReferenceException";
            bool threw = false;
            try { TsPlayer.ToPlayerApis(null); }
            catch (NullReferenceException) { threw = true; }

            LogResult(testName, threw, "threw=" + threw);
            Assert.IsTrue(threw);
        }

        [UnityTest]
        public IEnumerator ToPlayerIDs_ArrayContainingNullElement_ThrowsNullReferenceException()
        {
            // Throws from GetPlayerID(players[i]) inside the loop, a different
            // line than the whole-array-null case above (which throws on
            // players.Length before the loop is ever reached).
            const string testName = "ToPlayerIDs_ArrayContainingNullElement_ThrowsNullReferenceException";
            yield return StartClientSim();

            VRCPlayerApi[] input = { Networking.LocalPlayer, null };
            bool threw = false;
            try { TsPlayer.ToPlayerIDs(input); }
            catch (NullReferenceException) { threw = true; }

            LogResult(testName, threw, "threw=" + threw);
        }

        [UnityTest]
        public IEnumerator ToPlayerApis_IdsArrayContainingNullElement_NullElementTreatedAsNotFound()
        {
            // A null id element doesn't throw here: GetPlayerID(player) == null
            // is just an ordinary false comparison for every real player, so the
            // element is treated as not found rather than raising.
            const string testName = "ToPlayerApis_IdsArrayContainingNullElement_NullElementTreatedAsNotFound";
            yield return StartClientSim();

            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            string[] ids = { localId, null };
            VRCPlayerApi[] result = TsPlayer.ToPlayerApis(ids);

            bool passed = result.Length == 1 && result[0].playerId == Networking.LocalPlayer.playerId;
            LogResult(testName, passed, "length=" + result.Length);
        }

        [UnityTest]
        public IEnumerator GetPlayerID_LocalPlayer_ReturnsDisplayNameHashPlayerId()
        {
            const string testName = "GetPlayerID_LocalPlayer_ReturnsDisplayNameHashPlayerId";
            yield return StartClientSim();

            VRCPlayerApi local = Networking.LocalPlayer;
            string expected = local.displayName + "#" + local.playerId;
            string actual = TsPlayer.GetPlayerID(local);

            bool passed = expected == actual;
            LogResult(testName, passed, "expected=" + expected + " actual=" + actual);
        }

        [UnityTest]
        public IEnumerator GetPlayerID_RemotePlayer_ReturnsThatPlayersDisplayNameHashPlayerId()
        {
            const string testName = "GetPlayerID_RemotePlayer_ReturnsThatPlayersDisplayNameHashPlayerId";
            yield return StartClientSim();

            const string remoteName = "RemoteForGetPlayerID";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;

            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");

            string expected = remote.displayName + "#" + remote.playerId;
            string actual = TsPlayer.GetPlayerID(remote);

            bool passed = expected == actual && actual != TsPlayer.GetPlayerID(Networking.LocalPlayer);
            LogResult(testName, passed, "expected=" + expected + " actual=" + actual);
        }

        [UnityTest]
        public IEnumerator GetPlayerID_DisplayNameContainsHash_StillRoundTripsThroughFindPlayerByID()
        {
            const string testName = "GetPlayerID_DisplayNameContainsHash_StillRoundTripsThroughFindPlayerByID";
            yield return StartClientSim();

            const string weirdName = "Weird#Name#42";
            ClientSimMain.SpawnRemotePlayer(weirdName);
            yield return null;
            yield return null;

            VRCPlayerApi weird = FindPlayerByName(weirdName);
            Assert.IsNotNull(weird, "Remote player was not spawned.");

            string id = TsPlayer.GetPlayerID(weird);
            VRCPlayerApi resolved = TsPlayer.FindPlayerByID(id);

            bool passed = id.StartsWith(weirdName + "#") && resolved != null && resolved.playerId == weird.playerId;
            LogResult(testName, passed, "id=" + id + " resolvedIsWeird=" + (resolved == weird));
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_LocalPlayerId_ReturnsLocalPlayer()
        {
            const string testName = "FindPlayerByID_LocalPlayerId_ReturnsLocalPlayer";
            yield return StartClientSim();

            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            VRCPlayerApi found = TsPlayer.FindPlayerByID(localId);

            bool passed = found != null && found.playerId == Networking.LocalPlayer.playerId;
            LogResult(testName, passed, "localId=" + localId + " foundIsLocal=" + passed);
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_RemotePlayerId_ReturnsRemotePlayer()
        {
            const string testName = "FindPlayerByID_RemotePlayerId_ReturnsRemotePlayer";
            yield return StartClientSim();

            const string remoteName = "RemoteForFind";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");

            VRCPlayerApi found = TsPlayer.FindPlayerByID(TsPlayer.GetPlayerID(remote));

            bool passed = found != null && found.playerId == remote.playerId;
            LogResult(testName, passed, "foundId=" + (found != null ? found.playerId.ToString() : "null") +
                " expectedId=" + remote.playerId);
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_NonExistentId_ReturnsNull()
        {
            const string testName = "FindPlayerByID_NonExistentId_ReturnsNull";
            yield return StartClientSim();

            VRCPlayerApi found = TsPlayer.FindPlayerByID("NoSuchPlayer#99999");

            bool passed = found == null;
            LogResult(testName, passed, "found=" + (found == null ? "null" : found.displayName));
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_NullId_ReturnsNullWithoutThrowing()
        {
            const string testName = "FindPlayerByID_NullId_ReturnsNullWithoutThrowing";
            yield return StartClientSim();

            VRCPlayerApi found = TsPlayer.FindPlayerByID(null);

            bool passed = found == null;
            LogResult(testName, passed, "found=" + (found == null ? "null" : found.displayName));
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_AfterPlayerLeaves_ReturnsNull()
        {
            const string testName = "FindPlayerByID_AfterPlayerLeaves_ReturnsNull";
            yield return StartClientSim();

            const string remoteName = "RemoteThatLeavesForFind";
            ClientSimMain.SpawnRemotePlayer(remoteName);
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName(remoteName);
            Assert.IsNotNull(remote, "Remote player was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);

            ClientSimMain.RemovePlayer(remote);
            yield return null;
            yield return null;

            VRCPlayerApi found = TsPlayer.FindPlayerByID(remoteId);

            bool passed = found == null;
            LogResult(testName, passed, "remoteId=" + remoteId + " found=" + (found == null ? "null" : found.displayName));
        }

        [UnityTest]
        public IEnumerator GetAllPlayers_SoloClient_ReturnsArrayOfOneContainingLocalPlayer()
        {
            const string testName = "GetAllPlayers_SoloClient_ReturnsArrayOfOneContainingLocalPlayer";
            yield return StartClientSim();

            VRCPlayerApi[] all = TsPlayer.GetAllPlayers();

            bool passed = all.Length == 1 && all[0].playerId == Networking.LocalPlayer.playerId;
            LogResult(testName, passed, "length=" + all.Length);
        }

        [UnityTest]
        public IEnumerator GetAllPlayers_LocalPlusTwoRemote_ReturnsAllThreeAndMatchesGetPlayerCount()
        {
            const string testName = "GetAllPlayers_LocalPlusTwoRemote_ReturnsAllThreeAndMatchesGetPlayerCount";
            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RemoteA");
            ClientSimMain.SpawnRemotePlayer("RemoteB");
            yield return null;
            yield return null;

            VRCPlayerApi[] all = TsPlayer.GetAllPlayers();
            int nativeCount = VRCPlayerApi.GetPlayerCount();

            bool hasLocal = all.Any(p => p.playerId == Networking.LocalPlayer.playerId);
            bool hasA = all.Any(p => p.displayName == "RemoteA");
            bool hasB = all.Any(p => p.displayName == "RemoteB");

            bool passed = all.Length == 3 && all.Length == nativeCount && hasLocal && hasA && hasB;
            LogResult(testName, passed, "length=" + all.Length + " nativeCount=" + nativeCount +
                " hasLocal=" + hasLocal + " hasA=" + hasA + " hasB=" + hasB);
        }

        [UnityTest]
        public IEnumerator GetAllPlayerIDs_MatchesToPlayerIDsOfGetAllPlayers()
        {
            const string testName = "GetAllPlayerIDs_MatchesToPlayerIDsOfGetAllPlayers";
            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RemoteForIDsComposition");
            yield return null;
            yield return null;

            string[] allIds = TsPlayer.GetAllPlayerIDs();
            string[] composed = TsPlayer.ToPlayerIDs(TsPlayer.GetAllPlayers());

            bool passed = allIds.Length == composed.Length && !allIds.Except(composed).Any() && !composed.Except(allIds).Any();
            LogResult(testName, passed, "allIds=[" + string.Join(",", allIds) + "] composed=[" + string.Join(",", composed) + "]");
        }

        [UnityTest]
        public IEnumerator ToPlayerIDs_MultiplePlayers_MapsInInputOrder()
        {
            const string testName = "ToPlayerIDs_MultiplePlayers_MapsInInputOrder";
            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RemoteOrderA");
            ClientSimMain.SpawnRemotePlayer("RemoteOrderB");
            yield return null;
            yield return null;

            VRCPlayerApi remoteA = FindPlayerByName("RemoteOrderA");
            VRCPlayerApi remoteB = FindPlayerByName("RemoteOrderB");
            Assert.IsNotNull(remoteA);
            Assert.IsNotNull(remoteB);

            VRCPlayerApi[] input = { remoteB, Networking.LocalPlayer, remoteA };
            string[] result = TsPlayer.ToPlayerIDs(input);

            bool passed = result.Length == 3 &&
                result[0] == TsPlayer.GetPlayerID(remoteB) &&
                result[1] == TsPlayer.GetPlayerID(Networking.LocalPlayer) &&
                result[2] == TsPlayer.GetPlayerID(remoteA);
            LogResult(testName, passed, "result=[" + string.Join(",", result) + "]");
        }

        [UnityTest]
        public IEnumerator ToPlayerApis_AllIdsResolvable_ReturnsPlayersInInputOrder()
        {
            const string testName = "ToPlayerApis_AllIdsResolvable_ReturnsPlayersInInputOrder";
            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RemoteApiOrderA");
            ClientSimMain.SpawnRemotePlayer("RemoteApiOrderB");
            yield return null;
            yield return null;

            VRCPlayerApi remoteA = FindPlayerByName("RemoteApiOrderA");
            VRCPlayerApi remoteB = FindPlayerByName("RemoteApiOrderB");
            Assert.IsNotNull(remoteA);
            Assert.IsNotNull(remoteB);

            string[] ids = { TsPlayer.GetPlayerID(remoteB), TsPlayer.GetPlayerID(Networking.LocalPlayer), TsPlayer.GetPlayerID(remoteA) };
            VRCPlayerApi[] result = TsPlayer.ToPlayerApis(ids);

            bool passed = result.Length == 3 &&
                result[0].playerId == remoteB.playerId &&
                result[1].playerId == Networking.LocalPlayer.playerId &&
                result[2].playerId == remoteA.playerId;
            LogResult(testName, passed, "resultIds=[" + string.Join(",", result.Select(p => p.playerId)) + "]");
        }

        [UnityTest]
        public IEnumerator ToPlayerApis_MixOfValidAndInvalidIds_TrimsToOnlyResolvableOnesInOrder()
        {
            const string testName = "ToPlayerApis_MixOfValidAndInvalidIds_TrimsToOnlyResolvableOnesInOrder";
            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RemoteApiMix");
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName("RemoteApiMix");
            Assert.IsNotNull(remote);

            string[] ids = { "Bogus#1", TsPlayer.GetPlayerID(remote), "Bogus#2", TsPlayer.GetPlayerID(Networking.LocalPlayer) };
            VRCPlayerApi[] result = TsPlayer.ToPlayerApis(ids);

            bool passed = result.Length == 2 &&
                result[0].playerId == remote.playerId &&
                result[1].playerId == Networking.LocalPlayer.playerId;
            LogResult(testName, passed, "length=" + result.Length +
                (result.Length == 2 ? " ids=[" + result[0].playerId + "," + result[1].playerId + "]" : ""));
        }

        [UnityTest]
        public IEnumerator ToPlayerApis_AllIdsNonExistent_ReturnsEmptyArray()
        {
            const string testName = "ToPlayerApis_AllIdsNonExistent_ReturnsEmptyArray";
            yield return StartClientSim();

            string[] ids = { "Bogus#1", "Bogus#2" };
            VRCPlayerApi[] result = TsPlayer.ToPlayerApis(ids);

            bool passed = result.Length == 0;
            LogResult(testName, passed, "length=" + result.Length);
        }

        [UnityTest]
        public IEnumerator ToPlayerApis_DuplicateIds_ReturnsSamePlayerTwice()
        {
            const string testName = "ToPlayerApis_DuplicateIds_ReturnsSamePlayerTwice";
            yield return StartClientSim();

            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            string[] ids = { localId, localId };
            VRCPlayerApi[] result = TsPlayer.ToPlayerApis(ids);

            bool passed = result.Length == 2 &&
                result[0].playerId == Networking.LocalPlayer.playerId &&
                result[1].playerId == Networking.LocalPlayer.playerId;
            LogResult(testName, passed, "length=" + result.Length);
        }

        [UnityTest]
        public IEnumerator ToPlayerApis_RoundTripFromGetAllPlayerIDs_MatchesGetAllPlayersAsSet()
        {
            const string testName = "ToPlayerApis_RoundTripFromGetAllPlayerIDs_MatchesGetAllPlayersAsSet";
            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RoundTripA");
            ClientSimMain.SpawnRemotePlayer("RoundTripB");
            yield return null;
            yield return null;

            VRCPlayerApi[] all = TsPlayer.GetAllPlayers();
            VRCPlayerApi[] roundTripped = TsPlayer.ToPlayerApis(TsPlayer.GetAllPlayerIDs());

            var allIds = all.Select(p => p.playerId).OrderBy(id => id).ToArray();
            var roundTrippedIds = roundTripped.Select(p => p.playerId).OrderBy(id => id).ToArray();

            bool passed = allIds.SequenceEqual(roundTrippedIds);
            LogResult(testName, passed, "allIds=[" + string.Join(",", allIds) + "] roundTrippedIds=[" + string.Join(",", roundTrippedIds) + "]");
        }
    }
}
