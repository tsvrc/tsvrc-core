using System;
using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using Tsvrc.UdonTestKit;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Tracking.ReadyCheckProcess
{
    // PlayerTracker.OnBecameProcessOwner's real GetAllPlayers() scan needs a live VRCPlayerApi
    // list, exercised here together with ReadyCheckProcess's own reaction to it
    // (OnTrackingPlayersRemoved trimming _readyPlayerIds, and _readyCheckActive's correction for
    // a real new owner). ClientSim never organically dispatches VRC lifecycle callbacks to a
    // plain UdonSharpBehaviour, so these tests call them directly.
    public class ReadyCheckProcessAbandonmentPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator OnBecameProcessOwner_RealDepartedReadyPlayer_RemovedFromBothTrackedAndReadySets()
        {
            yield return Session.Start();
            VRCPlayerApi departing = Session.SpawnRemotePlayer("Departing");
            VRCPlayerApi staying = Session.SpawnRemotePlayer("Staying");
            string departingId = departing.displayName + "#" + departing.playerId;
            string stayingId = staying.displayName + "#" + staying.playerId;

            var process = CreateProcess<ReadyCheckProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            process.StartReadyCheck(new[] { departingId, stayingId });
            process.BroadcastAddReadyPlayer(departingId);

            Session.RemovePlayer(departing);
            PrivateFieldAccess.InvokeInstance(process, "OnBecameProcessOwner");

            Assert.IsFalse(Array.IndexOf(process.LastPlayerIds, departingId) >= 0,
                "The departed player must be removed from the tracked set by the real scan.");
            var readyIds = PrivateFieldAccess.GetField<string[]>(process, "_readyPlayerIds");
            Assert.IsFalse(Array.IndexOf(readyIds, departingId) >= 0,
                "The departed player must also be removed from the ready set via OnTrackingPlayersRemoved.");
        }

        [UnityTest]
        public IEnumerator OnBecameProcessOwner_RealNewOwner_ReadyCheckActiveCorrectedForRealTakeover()
        {
            yield return Session.Start();
            VRCPlayerApi oldOwner = Session.SpawnRemotePlayer("OldOwner");

            var process = CreateProcess<ReadyCheckProcessTestSubclass>();
            process.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            Session.SetOwner(oldOwner, process.gameObject);
            // What a non-owner holds: the synced running flag, and its own local flag still false.
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_readyCheckActive", false);

            Session.RemovePlayer(oldOwner);
            process.OnPlayerLeft(oldOwner);

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_readyCheckActive"),
                "OnBecameProcessOwner must correct the stale _readyCheckActive flag for the real new (local) owner.");
        }
    }
}
