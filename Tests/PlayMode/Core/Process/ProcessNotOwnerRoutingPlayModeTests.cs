using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.Doubles;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Process
{
    // Covers the "local player is genuinely not the Unity owner" half of StartProcess/StopProcess/
    // CompleteProcess and their Request* counterparts - the half that can't be reached in EditMode
    // (see ProcessTestBase and ProcessNetworkCallableTests). The "owner" half of every one of these
    // guards lives in EditMode's ProcessLifecycleTests/ProcessNetworkCallableTests instead - no
    // overlap between the two.
    //
    // A bare AddComponent<Process>() GameObject has no IClientSimSyncable component, so
    // ClientSimPlayerManager.IsOwner (what Networking.IsOwner routes to) falls back to comparing
    // against the instance master - always true for the local client by default. Every test here
    // attaches FakeOwnershipSyncable (via ProcessPlayModeTestBase.MakeGenuinelyNotUnityOwner) to
    // override that fallback for real.
    public class ProcessNotOwnerRoutingPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator StartProcess_GenuinelyNotUnityOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotUnityOwner(process, remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.StartProcess();

            Assert.IsFalse(process.IsProcessRunning(),
                "StartProcess must forward to the real owner via SendCustomNetworkEvent instead of " +
                "starting the process locally.");
            Assert.AreEqual(0, process.OnProcessStartedCount);
        }

        [UnityTest]
        public IEnumerator RequestStartProcess_GenuinelyNotUnityOwner_DiscardsWithoutExecuting()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotUnityOwner(process, remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.RequestStartProcess();

            Assert.AreEqual(0, process.OnProcessStartedCount,
                "A misrouted RequestStartProcess call must be discarded when the local player is genuinely " +
                "not the real Unity owner.");
        }

        [UnityTest]
        public IEnumerator StopProcess_GenuinelyNotProcessOwnerAndNotUnityOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotUnityOwner(process, remote);
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
        public IEnumerator RequestStopProcess_GenuinelyNotProcessOwnerAndNotUnityOwner_DiscardsWithoutExecuting()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotUnityOwner(process, remote);
            process.StartProcess(useProcessUpdate: false);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.RequestStopProcess(PrivateFieldAccess.GetField<int>(process, "_runGeneration"));

            Assert.IsTrue(process.IsProcessRunning(),
                "A misrouted RequestStopProcess call must be discarded when the local player is genuinely " +
                "neither the named process owner nor the real Unity owner.");
            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [UnityTest]
        public IEnumerator CompleteProcess_GenuinelyNotProcessOwnerAndNotUnityOwner_ForwardsInsteadOfExecutingLocally()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotUnityOwner(process, remote);
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
        public IEnumerator RequestCompleteProcess_GenuinelyNotProcessOwnerAndNotUnityOwner_DiscardsWithoutExecuting()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotUnityOwner(process, remote);
            process.StartProcess(useProcessUpdate: false);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.RequestCompleteProcess(PrivateFieldAccess.GetField<int>(process, "_runGeneration"));

            Assert.IsTrue(process.IsProcessRunning(),
                "A misrouted RequestCompleteProcess call must be discarded when the local player is genuinely " +
                "neither the named process owner nor the real Unity owner.");
            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }
    }
}
