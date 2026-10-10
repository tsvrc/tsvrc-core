using System.Collections;
using System.Linq;
using NUnit.Framework;
using Tsvrc.Player;
using Tsvrc.UdonTestKit;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Player
{
    // ToArray is the only member of TsPlayer with zero VRCPlayerApi dependency - every other
    // member needs a live player list, which only resolves under a real ClientSim session.
    public class TsPlayerPlayModeTests : ClientSimTestBase
    {
        [UnityTest]
        public IEnumerator GetAllPlayers_RealSpawnedPlayers_ReturnsEveryRealPlayer()
        {
            yield return Session.Start();
            Session.SpawnRemotePlayer("First");
            Session.SpawnRemotePlayer("Second");

            VRCPlayerApi[] players = TsPlayer.GetAllPlayers();

            Assert.AreEqual(3, players.Length,
                "Must include the real local player plus both real spawned remote players.");
            Assert.IsTrue(players.Any(p => p.displayName == "First"));
            Assert.IsTrue(players.Any(p => p.displayName == "Second"));
        }

        [UnityTest]
        public IEnumerator GetPlayerID_RealLocalPlayer_MatchesRealDisplayNameAndPlayerId()
        {
            yield return Session.Start();

            string id = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            Assert.AreEqual(Networking.LocalPlayer.displayName + "#" + Networking.LocalPlayer.playerId, id);
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_RealSpawnedPlayer_ReturnsTheSameRealPlayer()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("Findable");
            string id = TsPlayer.GetPlayerID(remote);

            VRCPlayerApi found = TsPlayer.FindPlayerByID(id);

            Assert.AreEqual(remote.playerId, found.playerId);
        }

        [UnityTest]
        public IEnumerator FindPlayerByID_RealDepartedPlayer_ReturnsNull()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("Departing");
            string id = TsPlayer.GetPlayerID(remote);
            Session.RemovePlayer(remote);

            VRCPlayerApi found = TsPlayer.FindPlayerByID(id);

            Assert.IsNull(found);
        }

        [UnityTest]
        public IEnumerator ToPlayerApis_RealMixOfPresentAndDepartedIds_ReturnsOnlyThePresentOnes()
        {
            yield return Session.Start();
            VRCPlayerApi staying = Session.SpawnRemotePlayer("Staying");
            VRCPlayerApi leaving = Session.SpawnRemotePlayer("Leaving");
            string stayingId = TsPlayer.GetPlayerID(staying);
            string leavingId = TsPlayer.GetPlayerID(leaving);
            Session.RemovePlayer(leaving);

            VRCPlayerApi[] result = TsPlayer.ToPlayerApis(new[] { stayingId, leavingId });

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(staying.playerId, result[0].playerId);
        }

        [UnityTest]
        public IEnumerator GetAllPlayerIDs_RealSpawnedPlayers_MatchesGetAllPlayersFormattedIndividually()
        {
            yield return Session.Start();
            Session.SpawnRemotePlayer("Remote");

            string[] ids = TsPlayer.GetAllPlayerIDs();
            VRCPlayerApi[] players = TsPlayer.GetAllPlayers();

            Assert.AreEqual(players.Length, ids.Length);
            for (int i = 0; i < players.Length; i++)
                Assert.AreEqual(TsPlayer.GetPlayerID(players[i]), ids[i]);
        }
    }
}
