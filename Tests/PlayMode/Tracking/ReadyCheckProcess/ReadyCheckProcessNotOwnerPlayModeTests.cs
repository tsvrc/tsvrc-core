using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using Tsvrc.UdonTestKit;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Tracking.ReadyCheckProcess
{
    // The not-owner paths need a GameObject the local player genuinely doesn't own, which only
    // ClientSim can produce, through Session.SetOwner; Edit Mode always reports the local
    // player as owner.
    public class ReadyCheckProcessNotOwnerPlayModeTests : ProcessPlayModeTestBase
    {
        private static string[] GetReadyPlayerIds(Tsvrc.Tracking.ReadyCheckProcess process)
        {
            return PrivateFieldAccess.GetField<string[]>(process, "_readyPlayerIds");
        }

        private static ReadyCheckProcessTestSubclass CreateConstructed()
        {
            var process = CreateProcess<ReadyCheckProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            return process;
        }

        [UnityTest]
        public IEnumerator BroadcastAddReadyPlayer_GenuinelyNotOwner_Rejected()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateConstructed();
            process.StartReadyCheck(new[] { "A" });
            Session.SetOwner(remote, process.gameObject);

            process.BroadcastAddReadyPlayer("A");

            CollectionAssert.IsEmpty(GetReadyPlayerIds(process));
        }

        [UnityTest]
        public IEnumerator BroadcastRemoveReadyPlayer_GenuinelyNotOwner_Rejected()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateConstructed();
            process.StartReadyCheck(new[] { "A", "B" });
            process.BroadcastAddReadyPlayer("A");
            Session.SetOwner(remote, process.gameObject);

            process.BroadcastRemoveReadyPlayer("A");

            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(process));
        }

        [UnityTest]
        public IEnumerator TickProcessUpdate_GenuinelyNotOwner_NeverPollsForCompletion()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateConstructed();
            process.StartReadyCheck(new[] { "A", "B" });
            // Every tracked player ready: an owner tick would complete the check right away.
            PrivateFieldAccess.SetField(process, "_readyPlayerIds", new[] { "A", "B" });
            Session.SetOwner(remote, process.gameObject);

            PrivateFieldAccess.SetField(process, "_nextTickDueAtRealTime", 0f);
            process._TickProcessUpdate();

            Assert.AreEqual(0, process.OnReadyCheckCompletedCount);
            Assert.IsTrue(process.IsProcessRunning());
        }

        [UnityTest]
        public IEnumerator OnTrackingPlayersRemoved_GenuinelyNotOwner_DoesNotMutateReadyPlayerIds()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateConstructed();
            Session.SetOwner(remote, process.gameObject);
            PrivateFieldAccess.SetField(process, "_readyPlayerIds", new[] { "A" });

            // _isBroadcasting stands in for the owner's broadcast arriving here, letting the
            // call past NotifyTrackedPlayersRemoved's caller guard to reach the hook under test.
            PrivateFieldAccess.SetField(process, "_isBroadcasting", true);
            process.NotifyTrackedPlayersRemoved(new[] { "A" });
            PrivateFieldAccess.SetField(process, "_isBroadcasting", false);

            CollectionAssert.AreEqual(new[] { "A" }, GetReadyPlayerIds(process),
                "A non-owner client must never mutate its own _readyPlayerIds from this hook.");
        }
    }
}
