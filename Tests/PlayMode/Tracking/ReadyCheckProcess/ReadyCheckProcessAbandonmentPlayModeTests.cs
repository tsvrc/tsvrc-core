using System;
using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Tracking.ReadyCheckProcess
{
    // PlayerTracker.OnOwnerAbandonedProcess's real GetAllPlayers() scan needs a live VRCPlayerApi
    // list, exercised here together with ReadyCheckProcess's own reaction to it
    // (OnTrackingPlayersRemoved trimming _readyPlayerIds, and _readyCheckActive's correction for
    // a real new owner). ClientSim never organically dispatches VRC lifecycle callbacks to a
    // plain UdonSharpBehaviour, so OnOwnerAbandonedProcess is invoked directly via reflection.
    public class ReadyCheckProcessAbandonmentPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator OnOwnerAbandonedProcess_RealDepartedReadyPlayer_RemovedFromBothTrackedAndReadySets()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Departing");
            Players.SpawnRemotePlayer("Staying");
            yield return null;
            yield return null;
            VRCPlayerApi departing = ClientSimPlayerEnvironment.FindPlayerByName("Departing");
            VRCPlayerApi staying = ClientSimPlayerEnvironment.FindPlayerByName("Staying");
            Assert.IsNotNull(departing, "Setup sanity check: Departing was not found.");
            Assert.IsNotNull(staying, "Setup sanity check: Staying was not found.");
            string departingId = departing.displayName + "#" + departing.playerId;
            string stayingId = staying.displayName + "#" + staying.playerId;

            var process = CreateProcess<ReadyCheckProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartReadyCheck(new[] { departingId, stayingId });
            process.BroadcastAddReadyPlayer(departingId);

            Players.RemovePlayer(departing);
            PrivateFieldAccess.InvokeInstance(process, "OnOwnerAbandonedProcess");

            Assert.IsFalse(Array.IndexOf(process.LastPlayerIds, departingId) >= 0,
                "The departed player must be removed from the tracked set by the real scan.");
            var readyIds = PrivateFieldAccess.GetField<string[]>(process, "_readyPlayerIds");
            Assert.IsFalse(Array.IndexOf(readyIds, departingId) >= 0,
                "The departed player must also be removed from the ready set via OnTrackingPlayersRemoved.");
        }

        [UnityTest]
        public IEnumerator OnOwnerAbandonedProcess_RealNewOwner_ReadyCheckActiveCorrectedForRealTakeover()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("OldOwner");
            yield return null;
            yield return null;
            VRCPlayerApi oldOwner = ClientSimPlayerEnvironment.FindPlayerByName("OldOwner");
            Assert.IsNotNull(oldOwner, "Setup sanity check: OldOwner was not found.");

            var process = CreateProcess<ReadyCheckProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            PrivateFieldAccess.InvokeInstance(process, "SetProcessOwner", oldOwner);
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_readyCheckActive", false);

            PrivateFieldAccess.InvokeInstance(process, "OnOwnerAbandonedProcess");

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_readyCheckActive"),
                "OnOwnerAbandonedProcess must correct the stale _readyCheckActive flag for the real new (local) owner.");
        }
    }
}
