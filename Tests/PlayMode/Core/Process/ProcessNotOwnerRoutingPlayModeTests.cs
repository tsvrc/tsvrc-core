using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.Doubles;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Process
{
    // The not-owner half of Start/Stop/CompleteProcess and their Request* counterparts.
    // Can't be reached in EditMode, where IsOwner defaults true - that owner half lives in
    // ProcessLifecycleTests/ProcessNetworkCallableTests instead.
    //
    // A bare AddComponent<Process>() has no IClientSimSyncable, so ClientSimPlayerManager.IsOwner
    // falls back to comparing against the instance master, true by default for the local client.
    // Each test attaches FakeOwnershipSyncable via MakeGenuinelyNotOwner to override that.
    public class ProcessNotOwnerRoutingPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator StartProcess_GenuinelyNotOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotOwner(process, remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.StartProcess();

            Assert.IsFalse(process.IsProcessRunning(),
                "StartProcess must forward to the real owner via SendCustomNetworkEvent instead of " +
                "starting the process locally.");
            Assert.AreEqual(0, process.OnProcessStartedCount);
        }

        [UnityTest]
        public IEnumerator RequestStartProcess_GenuinelyNotOwner_DiscardsWithoutExecuting()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotOwner(process, remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.RequestStartProcess();

            Assert.AreEqual(0, process.OnProcessStartedCount,
                "A misrouted RequestStartProcess call must be discarded when the local player is genuinely " +
                "not the real owner.");
        }

        [UnityTest]
        public IEnumerator StopProcess_GenuinelyNotProcessOwnerAndNotOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotOwner(process, remote);
            process.StartProcess(useProcessUpdate: false);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.StopProcess();

            Assert.IsTrue(process.IsProcessRunning(),
                "StopProcess must forward to the real owner via SendCustomNetworkEvent instead of stopping " +
                "the process locally.");
            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [UnityTest]
        public IEnumerator RequestStopProcess_GenuinelyNotProcessOwnerAndNotOwner_DiscardsWithoutExecuting()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotOwner(process, remote);
            process.StartProcess(useProcessUpdate: false);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.RequestStopProcess(PrivateFieldAccess.GetField<int>(process, "_runGeneration"));

            Assert.IsTrue(process.IsProcessRunning(),
                "A misrouted RequestStopProcess call must be discarded when the local player is genuinely " +
                "neither the named process owner nor the real owner.");
            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [UnityTest]
        public IEnumerator CompleteProcess_GenuinelyNotProcessOwnerAndNotOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotOwner(process, remote);
            process.StartProcess(useProcessUpdate: false);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.CompleteProcess();

            Assert.IsTrue(process.IsProcessRunning(),
                "CompleteProcess must forward to the real owner via SendCustomNetworkEvent instead of " +
                "completing the process locally.");
            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }

        [UnityTest]
        public IEnumerator RequestCompleteProcess_GenuinelyNotProcessOwnerAndNotOwner_DiscardsWithoutExecuting()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotOwner(process, remote);
            process.StartProcess(useProcessUpdate: false);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.RequestCompleteProcess(PrivateFieldAccess.GetField<int>(process, "_runGeneration"));

            Assert.IsTrue(process.IsProcessRunning(),
                "A misrouted RequestCompleteProcess call must be discarded when the local player is genuinely " +
                "neither the named process owner nor the real owner.");
            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }
    }
}
