using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Only the _active == false and (_active == true, player not ready) paths are
    // reachable in Edit Mode - every branch beyond the player-ready gate needs a
    // real VRCPlayerApi (GetTrackingData/GetPosition/GetRotation/TeleportTo), which
    // requires ClientSim. Those live in Tests/PlayMode/Player/HeadClipGuard/.
    public class HeadClipGuardPostLateUpdateTests : HeadClipGuardTestBase
    {
        [Test]
        public void Inactive_IsCompleteNoOp_EvenWithStateThatWouldOtherwiseViolate()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_active", false);
            SetField(guard, "_playerReady", true);
            SetField(guard, "_localPlayer", null);
            Invoke(guard, "AllocateArrays", 1);
            SetField(guard, "_candidateCount", 1);

            Assert.DoesNotThrow(() => guard.PostLateUpdate());

            Assert.IsFalse(GetField<bool>(guard, "_lastViolated"));
        }

        [Test]
        public void ActiveButPlayerNotReady_LocalPlayerNull_IsNoOp()
        {
            HeadClipGuard guard = CreateGuard();
            SetField(guard, "_active", true);
            SetField(guard, "_playerReady", false);
            SetField(guard, "_localPlayer", null);

            Assert.DoesNotThrow(() => guard.PostLateUpdate());

            Assert.IsFalse(GetField<bool>(guard, "_playerReady"));
        }
    }
}
