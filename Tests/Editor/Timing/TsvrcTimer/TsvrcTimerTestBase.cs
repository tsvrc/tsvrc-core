using Tsvrc.Timing;

namespace Tsvrc.Tests.Editor
{
    // Extends TsvrcProcessTestBase (Tests/Editor/Core/TsvrcProcess/TsvrcProcessTestBase.cs)
    // to reuse CreateProcess<T>/SeedAsOwner/ForceNextTickDueNow/TearDown instead of
    // duplicating them - TsvrcTimer IS a TsvrcProcess, so every dual-authority-bypass
    // trick that base provides applies here unchanged.
    public abstract class TsvrcTimerTestBase : TsvrcProcessTestBase
    {
        protected static int GetStartServerTimeMsField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_startServerTimeMs");

        protected static void SetStartServerTimeMsField(TsvrcTimer timer, int value) =>
            PrivateFieldAccess.SetField(timer, "_startServerTimeMs", value);

        protected static int GetElapsedOffsetMsField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_elapsedOffsetMs");

        protected static void SetElapsedOffsetMsField(TsvrcTimer timer, int value) =>
            PrivateFieldAccess.SetField(timer, "_elapsedOffsetMs", value);

        protected static bool GetWasCompletedField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_wasCompleted");

        protected static void SetWasCompletedField(TsvrcTimer timer, bool value) =>
            PrivateFieldAccess.SetField(timer, "_wasCompleted", value);

        protected static bool GetIsPausedField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_isPaused");

        protected static void SetIsPausedField(TsvrcTimer timer, bool value) =>
            PrivateFieldAccess.SetField(timer, "_isPaused", value);

        protected static int GetDurationMsField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_durationMs");

        protected static void SetDurationMsField(TsvrcTimer timer, int value) =>
            PrivateFieldAccess.SetField(timer, "_durationMs", value);

        protected static int GetRunIdField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_runId");

        protected static void SetRunIdField(TsvrcTimer timer, int value) =>
            PrivateFieldAccess.SetField(timer, "_runId", value);

        protected static bool GetLocalUpdateLoopActiveField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_localUpdateLoopActive");

        protected static bool GetHasObservedStateField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_hasObservedState");

        protected static int GetLastObservedElapsedMsField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<int>(timer, "_lastObservedElapsedMs");

        protected static bool GetLastObservedIsRunningField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_lastObservedIsRunning");

        protected static bool GetLastObservedIsPausedField(TsvrcTimer timer) =>
            PrivateFieldAccess.GetField<bool>(timer, "_lastObservedIsPaused");

        // Copies every synced field from one timer instance onto another, simulating a
        // single arrived [UdonSynced] deserialization packet without real networking -
        // same technique TsvrcProcessTests uses for its two-independent-clients race
        // simulation. Caller still has to invoke remote.OnDeserialization() themselves.
        protected static void CopySyncedFieldsTo(TsvrcTimer from, TsvrcTimer to)
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
