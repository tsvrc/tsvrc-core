using Tsvrc.Testing.Framework;
using Tsvrc.Timing;

namespace Tsvrc.Tests.EditMode
{
    // Extends ProcessTestBase (Tests/Editor/Core/Process/ProcessTestBase.cs)
    // to reuse CreateProcess<T>/SeedAsOwner/ForceNextTickDueNow/TearDown instead of
    // duplicating them - TsTimer IS a Process, so every dual-authority-bypass
    // trick that base provides applies here unchanged.
    public abstract class TsTimerTestBase : ProcessTestBase
    {
        protected static int GetStartServerTimeMsField(TsTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_startServerTimeMs");

        protected static void SetStartServerTimeMsField(TsTimer timer, int value) =>
            PrivateFieldAccess.SetField(timer, "_startServerTimeMs", value);

        protected static int GetElapsedOffsetMsField(TsTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_elapsedOffsetMs");

        protected static void SetElapsedOffsetMsField(TsTimer timer, int value) =>
            PrivateFieldAccess.SetField(timer, "_elapsedOffsetMs", value);

        protected static bool GetWasCompletedField(TsTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_wasCompleted");

        protected static void SetWasCompletedField(TsTimer timer, bool value) =>
            PrivateFieldAccess.SetField(timer, "_wasCompleted", value);

        protected static bool GetIsPausedField(TsTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_isPaused");

        protected static void SetIsPausedField(TsTimer timer, bool value) =>
            PrivateFieldAccess.SetField(timer, "_isPaused", value);

        protected static int GetDurationMsField(TsTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_durationMs");

        protected static void SetDurationMsField(TsTimer timer, int value) =>
            PrivateFieldAccess.SetField(timer, "_durationMs", value);

        protected static int GetRunIdField(TsTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_runId");

        protected static void SetRunIdField(TsTimer timer, int value) =>
            PrivateFieldAccess.SetField(timer, "_runId", value);

        protected static bool GetLocalUpdateLoopActiveField(TsTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_localUpdateLoopActive");

        protected static bool GetHasObservedStateField(TsTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_hasObservedState");

        protected static int GetLastObservedElapsedMsField(TsTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_lastObservedElapsedMs");

        protected static bool GetLastObservedIsRunningField(TsTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_lastObservedIsRunning");

        protected static bool GetLastObservedIsPausedField(TsTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_lastObservedIsPaused");

        // Copies every synced field from one timer instance onto another, simulating a
        // single arrived [UdonSynced] deserialization packet without real networking -
        // same technique ProcessTests uses for its two-independent-clients race
        // simulation. Caller still has to invoke remote.OnDeserialization() themselves.
        protected static void CopySyncedFieldsTo(TsTimer from, TsTimer to)
        {
            PrivateFieldAccess.SetField(to, "_isRunning", PrivateFieldAccess.GetField<bool>(from, "_isRunning"));
            PrivateFieldAccess.SetField(to, "_useProcessUpdate", PrivateFieldAccess.GetField<bool>(from, "_useProcessUpdate"));
            PrivateFieldAccess.SetField(to, "_startServerTimeMs", GetStartServerTimeMsField(from));
            PrivateFieldAccess.SetField(to, "_elapsedOffsetMs", GetElapsedOffsetMsField(from));
            PrivateFieldAccess.SetField(to, "_durationMs", GetDurationMsField(from));
            PrivateFieldAccess.SetField(to, "_wasCompleted", GetWasCompletedField(from));
            PrivateFieldAccess.SetField(to, "_isPaused", GetIsPausedField(from));
            PrivateFieldAccess.SetField(to, "_runId", GetRunIdField(from));
        }
    }
}
