using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.TsProcess;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Timing.TsTimer
{
    // GetServerTimeMilliseconds() only advances with real wall-clock time under a live
    // ClientSim session - a platform fact, not tsvrc logic, proven with a real WaitForSeconds
    // coroutine. PauseTimer/ResumeTimer's non-owner-forwarding branch needs a GameObject that
    // genuinely isn't Unity-owned by the local player - FakeOwnershipSyncable produces that for real.
    public class TsTimerPlayModeTests : TsProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator GetElapsedMilliseconds_RealRunningTimer_GrowsWithRealWallClockTime()
        {
            yield return StartClientSim();

            var timer = CreateProcess<TsTimerTestSubclass>();
            timer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            timer.StartTimer();

            int elapsedAtStart = timer.GetElapsedMilliseconds();
            yield return new WaitForSeconds(0.5f);

            int elapsedAfterWait = timer.GetElapsedMilliseconds();
            Assert.Greater(elapsedAfterWait, elapsedAtStart,
                "Elapsed time must grow with real wall-clock time under a live ClientSim session.");
        }

        [UnityTest]
        public IEnumerator PauseTimer_RealNotOwnerAndNotUnityOwner_ForwardsToOwnerInstead()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: RemoteOwner was not found.");

            var timer = CreateProcess<TsTimerTestSubclass>();
            timer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            MakeGenuinelyNotUnityOwner(timer, remote);
            timer.StartTimer();
            PrivateFieldAccess.InvokeInstance(timer, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(timer.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            timer.PauseTimer();

            Assert.AreEqual(0, timer.OnTimerPausedCount,
                "PauseTimer must forward to the real owner via SendCustomNetworkEvent instead of pausing locally.");
        }

        [UnityTest]
        public IEnumerator ResumeTimer_RealNotOwnerAndNotUnityOwner_ForwardsToOwnerInstead()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("RemoteOwner");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("RemoteOwner");
            Assert.IsNotNull(remote, "Setup sanity check: RemoteOwner was not found.");

            var timer = CreateProcess<TsTimerTestSubclass>();
            timer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            timer.StartTimer();
            PrivateFieldAccess.SetField(timer, "_isPaused", true);
            MakeGenuinelyNotUnityOwner(timer, remote);
            PrivateFieldAccess.InvokeInstance(timer, "SetProcessOwner", remote);
            Assert.IsFalse(Networking.IsOwner(timer.gameObject),
                "Setup sanity check: FakeOwnershipSyncable did not make the local player a genuine non-owner.");

            timer.ResumeTimer();

            Assert.AreEqual(0, timer.OnTimerResumedCount,
                "ResumeTimer must forward to the real owner via SendCustomNetworkEvent instead of resuming locally.");
        }
    }
}
