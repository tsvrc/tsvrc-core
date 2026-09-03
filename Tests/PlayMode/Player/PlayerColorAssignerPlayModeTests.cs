using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Player;
using Tsvrc.Testing.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Player
{
    // A bare GameObject has no IClientSimSyncable component, so Networking.IsOwner falls back
    // to the instance master and always reports the local player as owner - see
    // FakeOwnershipSyncable's own doc comment. Attaching it here is what makes "the local
    // player is genuinely not the owner" reachable at all, to verify Initialize's owner gate
    // (the owner-shuffles case is already covered in EditMode, where a bare GameObject is
    // unconditionally its own owner).
    public class PlayerColorAssignerPlayModeTests : TsPlayModeTestBase
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.Destroy(go);
            _spawned.Clear();
        }

        private PlayerColorAssigner CreateAssigner()
        {
            var go = new GameObject("Assigner");
            _spawned.Add(go);
            return go.AddComponent<PlayerColorAssigner>();
        }

        private static byte[] GetOrder(PlayerColorAssigner assigner) =>
            PrivateFieldAccess.GetField<byte[]>(assigner, "_order");

        [UnityTest]
        public IEnumerator Initialize_GenuinelyNotUnityOwner_DoesNotReshuffleOrder()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var assigner = CreateAssigner();
            var syncable = assigner.gameObject.AddComponent<FakeOwnershipSyncable>();
            syncable.InitializeOwner(remote.playerId);
            Assert.IsFalse(Networking.IsOwner(assigner.gameObject),
                "Setup sanity check: the local player must genuinely not be the owner.");

            assigner.Initialize();
            byte[] order = GetOrder(assigner);

            for (int i = 0; i < order.Length; i++)
                Assert.AreEqual(i, order[i],
                    "A non-owner's Initialize call must leave the order untouched (identity), " +
                    "not reshuffle - only the owner ever broadcasts a real shuffle.");
        }
    }
}
