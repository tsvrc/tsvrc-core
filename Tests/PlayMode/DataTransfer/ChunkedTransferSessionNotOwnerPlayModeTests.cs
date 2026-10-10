using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.DataTransfer
{
    // The not-owner paths need a GameObject the local player genuinely doesn't own, which only
    // FakeOwnershipSyncable under ClientSim can produce; Edit Mode always reports the local
    // player as owner.
    public class ChunkedTransferSessionNotOwnerPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator TickProcessUpdate_GenuinelyNotOwner_NeverReachesTheCancelCheck()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: RemoteOwner was not found.");
            string localId = Networking.LocalPlayer.displayName + "#" + Networking.LocalPlayer.playerId;

            var session = CreateProcess<ChunkedTransferSessionTestSubclass>();
            session.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            session.TransferData("hello", new[] { localId, "Other#1" });
            // An owner tick would cancel the transfer on this empty tracked list.
            PrivateFieldAccess.SetField(session, "_trackedPlayerIds", new string[0]);
            MakeGenuinelyNotOwner(session, remote);
            Assert.IsFalse(Networking.IsOwner(session.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            PrivateFieldAccess.SetField(session, "_nextTickDueAtRealTime", 0f);
            session._TickProcessUpdate();

            Assert.AreEqual(0, session.OnChunkSequenceStoppedCount);
            Assert.IsTrue(session.IsProcessRunning());
        }
    }
}
