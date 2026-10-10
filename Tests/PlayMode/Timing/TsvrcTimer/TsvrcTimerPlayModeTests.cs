using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using Tsvrc.UdonTestKit;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Timing.TsvrcTimer
{
    // GetServerTimeMilliseconds() only advances with real wall-clock time under a live
    // ClientSim session - a platform fact, not tsvrc logic, proven with a real WaitForSeconds
    // coroutine. PauseTimer/ResumeTimer's non-owner-forwarding branch needs a GameObject that
    // genuinely isn't Unity-owned by the local player - Session.SetOwner produces that for real.
    public class TsTimerPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator GetElapsedMilliseconds_RealRunningTimer_GrowsWithRealWallClockTime()
        {
            yield return Session.Start();

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
        public IEnumerator StartTimer_GenuinelyNotOwner_ClaimsOwnershipAndKeepsTheDuration()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var timer = CreateProcess<TsTimerTestSubclass>();
            timer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            Session.SetOwner(remote, timer.gameObject);

            timer.StartTimer(5000);

            Assert.IsTrue(Networking.IsOwner(timer.gameObject),
                "A non-owner's StartTimer must claim ownership, since the duration it writes never reaches the owner.");
            Assert.IsTrue(timer.IsProcessRunning());
            Assert.AreEqual(5000, timer.DurationMs);
        }

        [UnityTest]
        public IEnumerator PauseTimer_GenuinelyNotOwner_ForwardsToOwnerInstead()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var timer = CreateProcess<TsTimerTestSubclass>();
            timer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            timer.StartTimer();
            Session.SetOwner(remote, timer.gameObject);

            timer.PauseTimer();

            Assert.AreEqual(0, timer.OnTimerPausedCount,
                "PauseTimer must forward to the real owner via SendCustomNetworkEvent instead of pausing locally.");
        }

        [UnityTest]
        public IEnumerator ResumeTimer_GenuinelyNotOwner_ForwardsToOwnerInstead()
        {
            yield return Session.Start();
            VRCPlayerApi remote = Session.SpawnRemotePlayer("RemoteOwner");

            var timer = CreateProcess<TsTimerTestSubclass>();
            timer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            timer.StartTimer();
            PrivateFieldAccess.SetField(timer, "_isPaused", true);
            Session.SetOwner(remote, timer.gameObject);

            timer.ResumeTimer();

            Assert.AreEqual(0, timer.OnTimerResumedCount,
                "ResumeTimer must forward to the real owner via SendCustomNetworkEvent instead of resuming locally.");
        }
    }
}
