using Tsvrc.Tracking;

namespace Tsvrc.Tests.Editor
{
    // Extends PlayerTrackerTestBase (Tests/Editor/Tracking/PlayerTracker/PlayerTrackerTestBase.cs)
    // to reuse CreateProcess<T>/SeedAsOwner/GetTrackedPlayerIds/SetTrackedPlayerIds/TearDown
    // instead of duplicating them - ReadyCheckProcess IS a PlayerTracker (IS a TsProcess),
    // so every helper built for the base classes applies here unchanged.
    public abstract class ReadyCheckProcessTestBase : PlayerTrackerTestBase
    {
        protected static string[] GetReadyPlayerIds(ReadyCheckProcess process)
        {
            return PrivateFieldAccess.GetField<string[]>(process, "_readyPlayerIds");
        }

        protected static void SetReadyPlayerIds(ReadyCheckProcess process, string[] ids)
        {
            PrivateFieldAccess.SetField(process, "_readyPlayerIds", ids);
        }

        protected static bool GetReadyCheckActive(ReadyCheckProcess process)
        {
            return PrivateFieldAccess.GetField<bool>(process, "_readyCheckActive");
        }

        protected static void SetReadyCheckActive(ReadyCheckProcess process, bool value)
        {
            PrivateFieldAccess.SetField(process, "_readyCheckActive", value);
        }

        protected static bool InvokeIsPlayerReady(ReadyCheckProcess process, string playerId)
        {
            return (bool)PrivateFieldAccess.InvokeInstance(process, "IsPlayerReady", playerId);
        }
    }
}
