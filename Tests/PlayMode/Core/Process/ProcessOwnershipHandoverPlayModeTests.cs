using System.Collections;
using NUnit.Framework;
using Tsvrc.Player;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Process
{
    // The four VRC callback overrides (OnPlayerLeft, OnOwnershipTransferred,
    // OnPlayerSuspendChanged, OnDeserialization) need real Networking/VRCPlayerApi.
    //
    // ClientSim never organically dispatches VRC lifecycle callbacks to a plain
    // UdonSharpBehaviour (only a compiled UdonBehaviour VM instance registered with
    // UdonManager gets that), so these tests call the overrides directly against a genuine
    // VRCPlayerApi from a real ClientSim spawn, exercising TsPlayer.GetPlayerID/FindPlayerByID
    // against real player data instead of a hand-built string.
    //
    // A bare AddComponent<Process>() GameObject has no IClientSimSyncable component, so
    // ClientSimPlayerManager.IsOwner (what Networking.IsOwner routes to) falls back to
    // comparing against the instance master - always true for the local client by default.
    // Tests below that need the local player to genuinely NOT be the Unity owner attach
    // FakeOwnershipSyncable (via ProcessPlayModeTestBase.MakeGenuinelyNotUnityOwner) to
    // override that fallback for real.
    public class ProcessOwnershipHandoverPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator SetProcessOwner_ToRealSpawnedRemotePlayer_OwnerIdMatchesRealPlayerIdFormat()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);

            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);

            Assert.AreEqual(TsPlayer.GetPlayerID(remote), PrivateFieldAccess.GetField<string>(process, "_ownerId"));
            Assert.AreEqual(remote.playerId, PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt"));
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RealRemoteOwnerRemoved_LocalPlayerTakesOverAbandonedProcess()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote player was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);

            Players.RemovePlayer(remote);
            process.OnPlayerLeft(remote);

            Assert.AreEqual(1, process.OnBecameProcessOwnerCount);
            Assert.AreEqual(Networking.LocalPlayer.playerId,
                PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt"),
                "TakeOverRunningProcess must hand ownership to the real local player.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_RealSuspendedNamedOwnerAndGenuinelyNotUnityOwner_ClaimsOwnershipViaRealSetOwner()
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

            remote.GetClientSimPlayer().isSuspended = true;
            process.OnPlayerSuspendChanged(remote);

            Assert.IsTrue(Networking.IsOwner(process.gameObject),
                "OnPlayerSuspendChanged's real Networking.SetOwner(Networking.LocalPlayer, ...) call must hand " +
                "Unity ownership to the local player.");
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

            process.RequestStopProcess();

            Assert.IsTrue(process.IsProcessRunning(),
                "A misrouted RequestStopProcess call must be discarded when the local player is genuinely " +
                "neither the named process owner nor the real Unity owner.");
            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }
    }
}
