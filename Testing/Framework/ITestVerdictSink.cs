using System.Collections.Generic;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Assert failures inside a Play Mode test are not surfaced by Unity Test Framework
    /// here (see BatchModeTerminationFixup for why), so every test computes its own
    /// pass/fail bool and records it through this sink instead of relying on the Test
    /// Runner's own signal. Generalizes the LogResult/PLAYMODE_TEST_RESULT pattern every
    /// PlayMode file used to hand-roll independently.
    /// </summary>
    public interface ITestVerdictSink
    {
        void Record(string testName, bool passed, string details);

        int ResultCount { get; }

        IReadOnlyList<string> FailedNames { get; }

        /// <summary>Logs the final tallied verdict for the whole suite. Called once, from [OneTimeTearDown].</summary>
        void Report();
    }
}
