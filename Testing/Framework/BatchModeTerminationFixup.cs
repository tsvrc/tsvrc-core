using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// VRCSDK's own UnityEventFilter strips Unity Test Framework's internal
    /// TestStarted/TestFinished/RunStarted/RunFinished event wiring the moment Play Mode
    /// starts (it targets a "prohibited type" called PlayModeRunnerCallback) - that's what
    /// makes the Test Runner window never learn a Play Mode run happened at all, let alone
    /// whether it passed. The same stripped RunFinished event is also what Unity Test
    /// Framework normally uses to know a run is over: both to auto-exit Play Mode
    /// interactively, and (in batch mode) to tell -runTests it's safe to write
    /// results.xml and quit the whole process. Without it, an interactive session just
    /// sits in Play Mode forever, and a batch-mode run sits there idle indefinitely - not
    /// hung, just waiting on a completion signal that will never arrive.
    ///
    /// Interactively, EditorApplication.isPlaying = false (standard public API) exits Play
    /// Mode cleanly. In batch mode that same call triggers Unity's own scene-restore/lighting
    /// step, which hangs under -nographics - so batch mode instead calls
    /// EditorApplication.Exit() directly, terminating the process immediately and giving CI
    /// a real, standard exit code (0 pass / 1 fail) instead of depending on results.xml,
    /// which never gets written here either, for the same underlying reason.
    /// </summary>
    public sealed class BatchModeTerminationFixup : IPlayModeEnvironmentFixup
    {
        // Injectable seams so BatchModeTerminationFixupTests can verify the branching logic
        // without actually exiting the process or dropping out of Play Mode. Default to the
        // real statics; only tests (via InternalsVisibleTo) ever override these.
        internal Func<bool> IsBatchMode = () => Application.isBatchMode;
        internal Action<int> ExitBatchMode = code =>
        {
#if UNITY_EDITOR
            EditorApplication.Exit(code);
#endif
        };
        internal Action StopPlaying = () =>
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#endif
        };

        public void OnBeforeAnyTests()
        {
        }

        public void OnUnityTearDown()
        {
        }

        public void OnAfterAllTests(bool allPassed)
        {
            if (IsBatchMode())
                ExitBatchMode(allPassed ? 0 : 1);
            else
                StopPlaying();
        }
    }
}
