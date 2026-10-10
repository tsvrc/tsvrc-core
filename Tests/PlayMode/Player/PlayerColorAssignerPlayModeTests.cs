using System.Collections;
using NUnit.Framework;
using Tsvrc.Player;
using Tsvrc.UdonTestKit;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Player
{
    // ClientSim makes the master the owner of a bare GameObject, so without Session.SetOwner the
    // local player always owns the assigner. Handing it to a remote player is what makes "the
    // local player is genuinely not the owner" reachable at all, to verify Initialize's owner gate
    // (the owner-shuffles case is already covered in EditMode, where a bare GameObject is
    // unconditionally its own owner).
    public class PlayerColorAssignerPlayModeTests : ClientSimTestBase
    {
        public PlayerColorAssignerPlayModeTests() : base(TestScene.Empty("Tsvrc.Tests.PlayMode.PlayerColorAssigner"))
        {
        }

        private static byte[] GetOrder(PlayerColorAssigner assigner) =>
            PrivateFieldAccess.GetField<byte[]>(assigner, "_order");

        [UnityTest]
        public IEnumerator Initialize_GenuinelyNotUnityOwner_DoesNotReshuffleOrder()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var assigner = new GameObject("Assigner").AddComponent<PlayerColorAssigner>();
            Session.SetOwner(remote, assigner.gameObject);

            assigner.Initialize();
            byte[] order = GetOrder(assigner);

            for (int i = 0; i < order.Length; i++)
                Assert.AreEqual(i, order[i],
                    "A non-owner's Initialize call must leave the order untouched (identity), " +
                    "not reshuffle - only the owner ever broadcasts a real shuffle.");
        }
    }
}
