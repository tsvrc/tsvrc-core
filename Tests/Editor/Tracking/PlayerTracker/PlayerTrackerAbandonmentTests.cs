using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    // OnPlayerLeft/OnPlayerSuspendChanged/OnOwnerAbandonedProcess all fundamentally need a
    // real VRCPlayerApi or a live player list to exercise their interesting branches - those
    // are covered in Tests/PlayMode/Tracking/PlayerTracker/ with ClientSim. This file only
    // covers the one branch genuinely reachable without any of that:
    // OnOwnerAbandonedProcess's empty-tracked-list early return, which returns before ever
    // calling TsPlayer.GetAllPlayers().
    //
    // OnPlayerLeft/OnPlayerSuspendChanged are NOT tested here at all, even for their
    // early-return branches: Tests/Editor/Tsvrc.Tests.Editor.asmdef references
    // "VRCSDKBase-Editor.dll" but not "VRCSDKBase.dll" (only Tests/PlayMode/
    // Tsvrc.Tests.PlayMode.asmdef does), and VRCPlayerApi is defined in the latter, so a
    // VRCPlayerApi parameter - even a literal `null` - fails to compile in this assembly.
    // Both methods' "not running" early-return branches are covered in Play Mode instead,
    // where the type resolves and a real player is available anyway.
    public class PlayerTrackerAbandonmentTests : PlayerTrackerTestBase
    {
        [Test]
        public void OnOwnerAbandonedProcess_EmptyTrackedList_ReturnsBeforeTouchingGetAllPlayers()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new string[0]);

            Assert.DoesNotThrow(() => PrivateFieldAccess.InvokeInstance(tracker, "OnOwnerAbandonedProcess"));
        }
    }
}
