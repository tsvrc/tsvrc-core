using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.Doubles;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Process
{
    // Covers real ownership handover - SetProcessOwner, OnPlayerLeft, OnPlayerSuspendChanged -
    // between real ClientSim-spawned players. The not-owner routing guards on Start/Stop/
    // CompleteProcess live in ProcessNotOwnerRoutingPlayModeTests instead.
    //
    // ClientSim only dispatches VRC lifecycle callbacks to a compiled UdonBehaviour VM instance,
    // not a plain UdonSharpBehaviour, so these tests call the overrides directly.
    //
    // A bare AddComponent<Process>() has no IClientSimSyncable, so ClientSimPlayerManager.IsOwner
    // falls back to comparing against the instance master, true by default for the local client.
    // Tests needing a genuine non-owner attach FakeOwnershipSyncable via MakeGenuinelyNotOwner.
    public class ProcessOwnershipHandoverPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator SetProcessOwner_ToRealSpawnedRemotePlayer_TransfersRealOwnership()
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

            Assert.AreEqual(remote.playerId, Networking.GetOwner(process.gameObject).playerId);
            Assert.IsFalse(Networking.IsOwner(process.gameObject));
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
            Assert.IsTrue(Networking.IsOwner(process.gameObject),
                "TakeOverRunningProcess must hand ownership to the real local player.");
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RealRemoteOwnerRemoved_RunGenerationSurvivesTheHandoverUnchanged()
        {
            // TakeOverRunningProcess continues the existing run, not a new one, so _runGeneration
            // (only ExecuteStart touches it) must be unchanged after the handover - otherwise an
            // in-flight request for this run would look stale to the new owner.
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
            int generationBeforeHandover = PrivateFieldAccess.GetField<int>(process, "_runGeneration");

            Players.RemovePlayer(remote);
            process.OnPlayerLeft(remote);

            Assert.AreEqual(generationBeforeHandover,
                PrivateFieldAccess.GetField<int>(process, "_runGeneration"),
                "The run generation must not change just because ownership moved to a new client.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_RealSuspendedNamedOwnerAndGenuinelyNotOwner_ClaimsOwnershipViaRealSetOwner()
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

            remote.GetClientSimPlayer().isSuspended = true;
            process.OnPlayerSuspendChanged(remote);

            Assert.IsTrue(Networking.IsOwner(process.gameObject),
                "OnPlayerSuspendChanged's real Networking.SetOwner(Networking.LocalPlayer, ...) call must hand " +
                "ownership to the local player.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_PlayerWokeUp_DoesNotClaimOwnership()
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

            // isSuspended left false: this is the wakeup event, not the suspend event.
            process.OnPlayerSuspendChanged(remote);

            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "A wakeup event must not claim ownership - only OnPlayerSuspendChanged(isSuspended: true) does.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_ProcessNotRunning_DoesNotClaimOwnership()
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
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");
            Assert.IsFalse(process.IsProcessRunning(), "Setup sanity check: the process was never started.");

            remote.GetClientSimPlayer().isSuspended = true;
            process.OnPlayerSuspendChanged(remote);

            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "A suspended owner of a process that isn't running must not trigger a claim.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_SuspendedPlayerIsNotTheProcessOwner_DoesNotClaimOwnership()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            Players.SpawnRemotePlayer("Bystander");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            VRCPlayerApi bystander = ClientSimPlayerEnvironment.FindPlayerByName("Bystander");
            Assert.IsNotNull(remote, "Setup sanity check: the spawned remote owner was not found.");
            Assert.IsNotNull(bystander, "Setup sanity check: the spawned bystander was not found.");

            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotOwner(process, remote);
            process.StartProcess(useProcessUpdate: false);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            // Bystander is suspended, but RemoteOwner - not Bystander - owns the process.
            bystander.GetClientSimPlayer().isSuspended = true;
            process.OnPlayerSuspendChanged(bystander);

            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "A suspended player who doesn't own the process must not trigger a claim.");
        }

        [UnityTest]
        public IEnumerator OnDeserialization_GenuinelyNotOwner_DoesNotRestartTheLoop()
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
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            process.OnDeserialization();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"),
                "A non-owner receiving a sync packet must not start the tick loop for itself.");
        }
    }
}
