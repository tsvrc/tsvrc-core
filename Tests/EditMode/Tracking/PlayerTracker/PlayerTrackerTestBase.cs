using Tsvrc.Testing.Framework;
using Tsvrc.Tracking;

namespace Tsvrc.Tests.EditMode
{
    // Extends TsProcessTestBase (Tests/Editor/Core/TsProcess/TsProcessTestBase.cs)
    // to reuse CreateProcess<T>/SeedAsOwner/TearDown instead of duplicating them -
    // PlayerTracker IS a TsProcess, so every dual-authority-bypass trick that base
    // provides applies here unchanged.
    public abstract class PlayerTrackerTestBase : TsProcessTestBase
    {
        protected static string[] GetTrackedPlayerIds(PlayerTracker tracker)
        {
            return (string[])PrivateFieldAccess.InvokeInstance(tracker, "GetTrackedPlayerIds");
        }

        protected static void SetTrackedPlayerIds(PlayerTracker tracker, string[] ids)
        {
            PrivateFieldAccess.SetField(tracker, "_trackedPlayerIds", ids);
        }

        protected static string[] GetInitialTrackerPlayerIds(PlayerTracker tracker)
        {
            return PrivateFieldAccess.GetField<string[]>(tracker, "_initialTrackerPlayerIds");
        }

        protected static bool InvokeIsTrackedPlayer(PlayerTracker tracker, string playerId)
        {
            return (bool)PrivateFieldAccess.InvokeInstance(tracker, "IsTrackedPlayer", playerId);
        }
    }
}
