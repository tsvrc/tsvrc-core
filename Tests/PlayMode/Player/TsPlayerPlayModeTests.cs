using System.Collections;
using System.Linq;
using NUnit.Framework;
using Tsvrc.Player;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Player
{
    // ToArray is the only member of TsPlayer with zero VRCPlayerApi dependency - every other
    // member needs a live player list, which only resolves under a real ClientSim session.
    public class TsPlayerPlayModeTests : TsPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator GetAllPlayers_RealSpawnedPlayers_ReturnsEveryRealPlayer()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("First");
            Players.SpawnRemotePlayer("Second");
            yield return null;
            yield return null;

            VRCPlayerApi[] players = TsPlayer.GetAllPlayers();

            Assert.AreEqual(3, players.Length,
                "Must include the real local player plus both real spawned remote players.");
            Assert.IsTrue(players.Any(p => p.displayName == "First"));
            Assert.IsTrue(players.Any(p => p.displayName == "Second"));
        }

        [UnityTest]
        public IEnumerator GetPlayerID_RealLocalPlayer_MatchesRealDisplayNameAndPlayerId()
        {
            yield return StartClientSim();

            string id = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            Assert.AreEqual(Networking.LocalPlayer.displayName + "#" + Networking.LocalPlayer.playerId, id);
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_RealSpawnedPlayer_ReturnsTheSameRealPlayer()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Findable");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("Findable");
            Assert.IsNotNull(remote, "Setup sanity check: Findable was not found.");
            string id = TsPlayer.GetPlayerID(remote);

            VRCPlayerApi found = TsPlayer.FindPlayerByID(id);

            Assert.AreEqual(remote.playerId, found.playerId);
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_RealDepartedPlayer_ReturnsNull()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Departing");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("Departing");
            Assert.IsNotNull(remote, "Setup sanity check: Departing was not found.");
            string id = TsPlayer.GetPlayerID(remote);
            Players.RemovePlayer(remote);

            VRCPlayerApi found = TsPlayer.FindPlayerByID(id);

            Assert.IsNull(found);
        }

        [UnityTest]
        public IEnumerator ToPlayerApis_RealMixOfPresentAndDepartedIds_ReturnsOnlyThePresentOnes()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Staying");
            Players.SpawnRemotePlayer("Leaving");
            yield return null;
            yield return null;
            VRCPlayerApi staying = ClientSimPlayerEnvironment.FindPlayerByName("Staying");
            VRCPlayerApi leaving = ClientSimPlayerEnvironment.FindPlayerByName("Leaving");
            string stayingId = TsPlayer.GetPlayerID(staying);
            string leavingId = TsPlayer.GetPlayerID(leaving);
            Players.RemovePlayer(leaving);

            VRCPlayerApi[] result = TsPlayer.ToPlayerApis(new[] { stayingId, leavingId });

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(staying.playerId, result[0].playerId);
        }

        [UnityTest]
        public IEnumerator GetAllPlayerIDs_RealSpawnedPlayers_MatchesGetAllPlayersFormattedIndividually()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Remote");
            yield return null;
            yield return null;

            string[] ids = TsPlayer.GetAllPlayerIDs();
            VRCPlayerApi[] players = TsPlayer.GetAllPlayers();

            Assert.AreEqual(players.Length, ids.Length);
            for (int i = 0; i < players.Length; i++)
                Assert.AreEqual(TsPlayer.GetPlayerID(players[i]), ids[i]);
        }
    }
}
