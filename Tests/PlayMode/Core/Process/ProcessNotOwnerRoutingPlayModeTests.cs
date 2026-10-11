using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.Doubles;
using Tsvrc.UdonTestKit;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Process
{
    // The not-owner half of Start/Stop/CompleteProcess and their Request* counterparts.
    // Can't be reached in EditMode, where IsOwner defaults true - that owner half lives in
    // ProcessLifecycleTests/ProcessNetworkCallableTests instead.
    //
    // ClientSim makes the master the owner of every GameObject without VRCObjectSync or
    // VRCObjectPool, so each test hands the process to a remote player with Session.SetOwner.
    public class ProcessNotOwnerRoutingPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator StartProcess_GenuinelyNotOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            Session.SetOwner(remote, process.gameObject);

            process.StartProcess();

            Assert.IsFalse(process.IsProcessRunning(),
                "StartProcess must forward to the real owner via SendCustomNetworkEvent instead of " +
                "starting the process locally.");
            Assert.AreEqual(0, process.OnProcessStartedCount);
        }

        [UnityTest]
        public IEnumerator RequestStartProcess_GenuinelyNotOwner_DiscardsWithoutExecuting()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            Session.SetOwner(remote, process.gameObject);

            process.RequestStartProcess(false);

            Assert.AreEqual(0, process.OnProcessStartedCount,
                "A misrouted RequestStartProcess call must be discarded when the local player is genuinely " +
                "not the real owner.");
        }

        [UnityTest]
        public IEnumerator StopProcess_GenuinelyNotOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);

            process.StopProcess();

            Assert.IsTrue(process.IsProcessRunning(),
                "StopProcess must forward to the real owner via SendCustomNetworkEvent instead of stopping " +
                "the process locally.");
            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [UnityTest]
        public IEnumerator RequestStopProcess_GenuinelyNotOwner_DiscardsWithoutExecuting()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);

            process.RequestStopProcess(PrivateFieldAccess.GetField<int>(process, "_runGeneration"));

            Assert.IsTrue(process.IsProcessRunning(),
                "A misrouted RequestStopProcess call must be discarded when the local player is genuinely " +
                "not the real owner.");
            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [UnityTest]
        public IEnumerator CompleteProcess_GenuinelyNotOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);

            process.CompleteProcess();

            Assert.IsTrue(process.IsProcessRunning(),
                "CompleteProcess must forward to the real owner via SendCustomNetworkEvent instead of " +
                "completing the process locally.");
            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }

        [UnityTest]
        public IEnumerator RequestCompleteProcess_GenuinelyNotOwner_DiscardsWithoutExecuting()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);

            process.RequestCompleteProcess(PrivateFieldAccess.GetField<int>(process, "_runGeneration"));

            Assert.IsTrue(process.IsProcessRunning(),
                "A misrouted RequestCompleteProcess call must be discarded when the local player is genuinely " +
                "not the real owner.");
            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }
    }
}
