using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Testing.Framework
{
    // ClientSimPlayerEnvironment/TsPlayModeTestBase are the real-player scene/session plumbing
    // every other PlayMode test in this repo (and any consuming world's own) relies on
    // implicitly via StartClientSim()/Players - these prove that plumbing itself works, rather
    // than assuming it because everything built on top of it happens to pass.
    public class ClientSimPlayerEnvironmentPlayModeTests : TsPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator StartClientSim_LocalPlayerIsMasterTrue_LocalPlayerExistsAndIsMaster()
        {
            yield return StartClientSim(localPlayerIsMaster: true);

            Assert.IsNotNull(Networking.LocalPlayer);
            Assert.IsTrue(Networking.LocalPlayer.isLocal);
            Assert.IsTrue(Networking.IsMaster);
        }

        [UnityTest]
        public IEnumerator StartClientSim_LocalPlayerIsMasterFalse_LocalPlayerExistsAndIsNotMaster()
        {
            yield return StartClientSim(localPlayerIsMaster: false);

            Assert.IsNotNull(Networking.LocalPlayer);
            Assert.IsFalse(Networking.IsMaster);
        }

        [UnityTest]
        public IEnumerator StartClientSim_Default_LocalPlayerIsMaster()
        {
            // Default parameter value (localPlayerIsMaster = true) exercised with no explicit
            // argument, mirroring how most PlayMode tests in this repo call StartClientSim().
            yield return StartClientSim();

            Assert.IsTrue(Networking.IsMaster);
        }

        [UnityTest]
        public IEnumerator SpawnRemotePlayer_ThenFindPlayerByName_FindsTheRealPlayer()
        {
            yield return StartClientSim();

            Players.SpawnRemotePlayer("Remote1");
            yield return null;
            yield return null;

            VRCPlayerApi found = ClientSimPlayerEnvironment.FindPlayerByName("Remote1");
            Assert.IsNotNull(found);
            Assert.AreEqual("Remote1", found.displayName);
            Assert.IsFalse(found.isLocal);
        }

        [UnityTest]
        public IEnumerator FindPlayerByName_UnknownName_ReturnsNull()
        {
            yield return StartClientSim();

            Assert.IsNull(ClientSimPlayerEnvironment.FindPlayerByName("NoSuchPlayer"));
        }

        [UnityTest]
        public IEnumerator RemovePlayer_RemovedRemotePlayer_NoLongerFoundByName()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("ToRemove");
            yield return null;
            yield return null;
            VRCPlayerApi player = ClientSimPlayerEnvironment.FindPlayerByName("ToRemove");
            Assert.IsNotNull(player, "Setup sanity check: player was not found before removal.");

            Players.RemovePlayer(player);
            yield return null;

            Assert.IsNull(ClientSimPlayerEnvironment.FindPlayerByName("ToRemove"));
        }

        [UnityTest]
        public IEnumerator SpawnRemotePlayer_MultiplePlayers_AllIndependentlyFindable()
        {
            yield return StartClientSim();

            Players.SpawnRemotePlayer("Alpha");
            Players.SpawnRemotePlayer("Beta");
            yield return null;
            yield return null;

            VRCPlayerApi alpha = ClientSimPlayerEnvironment.FindPlayerByName("Alpha");
            VRCPlayerApi beta = ClientSimPlayerEnvironment.FindPlayerByName("Beta");
            Assert.IsNotNull(alpha);
            Assert.IsNotNull(beta);
            Assert.AreNotEqual(alpha.playerId, beta.playerId);
        }
    }
}
