using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.Doubles;
using Tsvrc.UdonTestKit;
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
    // ClientSim makes the master the owner of every GameObject without VRCObjectSync or
    // VRCObjectPool, and ignores Networking.SetOwner on it, so each test gives the process an
    // owner with Session.SetOwner before ownership has to move.
    public class ProcessOwnershipHandoverPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator SetProcessOwner_ToRealSpawnedRemotePlayer_TransfersRealOwnership()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            Session.SetOwner(Networking.LocalPlayer, process.gameObject);

            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", remote);

            Assert.AreEqual(remote.playerId, Networking.GetOwner(process.gameObject).playerId);
            Assert.IsFalse(Networking.IsOwner(process.gameObject));
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RealRemoteOwnerRemoved_LocalPlayerTakesOverAbandonedProcess()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);
            // The local tick that notices the lost ownership, which SendCustomEventDelayedSeconds
            // never runs outside Udon.
            process._TickProcessUpdate();

            Session.RemovePlayer(remote);
            process.OnPlayerLeft(remote);

            Assert.AreEqual(1, process.OnBecameProcessOwnerCount,
                "Ownership of the departed owner's process passes to the local master, which must take over the run.");
        }

        [UnityTest]
        public IEnumerator OnPlayerLeft_RealRemoteOwnerRemoved_RunGenerationSurvivesTheHandoverUnchanged()
        {
            // TakeOverRunningProcess continues the existing run, not a new one, so _runGeneration
            // (only ExecuteStart touches it) must be unchanged after the handover - otherwise an
            // in-flight request for this run would look stale to the new owner.
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);
            process._TickProcessUpdate();
            int generationBeforeHandover = PrivateFieldAccess.GetField<int>(process, "_runGeneration");

            Session.RemovePlayer(remote);
            process.OnPlayerLeft(remote);

            Assert.AreEqual(1, process.OnBecameProcessOwnerCount, "Setup sanity check: the handover must have happened.");
            Assert.AreEqual(generationBeforeHandover,
                PrivateFieldAccess.GetField<int>(process, "_runGeneration"),
                "The run generation must not change just because ownership moved to a new client.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_RealSuspendedOwnerWhileRunning_ClaimsOwnershipViaRealSetOwner()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);

            remote.GetClientSimPlayer().isSuspended = true;
            process.OnPlayerSuspendChanged(remote);

            Assert.IsTrue(Networking.IsOwner(process.gameObject),
                "OnPlayerSuspendChanged's real Networking.SetOwner(Networking.LocalPlayer, ...) call must hand " +
                "ownership to the local player.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_PlayerWokeUp_DoesNotClaimOwnership()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);

            // isSuspended left false: this is the wakeup event, not the suspend event.
            process.OnPlayerSuspendChanged(remote);

            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "A wakeup event must not claim ownership - only OnPlayerSuspendChanged(isSuspended: true) does.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_ProcessNotRunning_DoesNotClaimOwnership()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            Session.SetOwner(remote, process.gameObject);

            remote.GetClientSimPlayer().isSuspended = true;
            process.OnPlayerSuspendChanged(remote);

            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "A suspended owner of a process that isn't running must not trigger a claim.");
        }

        [UnityTest]
        public IEnumerator OnPlayerSuspendChanged_SuspendedPlayerIsNotTheProcessOwner_DoesNotClaimOwnership()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            VRCPlayerApi bystander = Session.SpawnRemotePlayer("Bystander");
            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartProcess(useProcessUpdate: false);
            Session.SetOwner(remote, process.gameObject);

            // Bystander is suspended, but RemoteOwner - not Bystander - owns the process.
            bystander.GetClientSimPlayer().isSuspended = true;
            process.OnPlayerSuspendChanged(bystander);

            Assert.IsFalse(Networking.IsOwner(process.gameObject),
                "A suspended player who doesn't own the process must not trigger a claim.");
        }

        [UnityTest]
        public IEnumerator OnDeserialization_GenuinelyNotOwner_DoesNotRestartTheLoop()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");
            var process = CreateProcess<ProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            Session.SetOwner(remote, process.gameObject);
            PrivateFieldAccess.SetField(process, "_isRunning", true);

            process.OnDeserialization();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"),
                "A non-owner receiving a sync packet must not start the tick loop for itself.");
        }
    }
}
