using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Shared base for real, ClientSim-backed Play Mode tests. Applies every active
    /// IPlayModeEnvironmentFixup at the matching lifecycle point, tallies pass/fail
    /// through an ITestVerdictSink (Unity's own RunFinished signal is stripped by
    /// VRCSDK's UnityEventFilter - see BatchModeTerminationFixup), and terminates the run
    /// correctly regardless of whether that happened interactively or in batch mode. Extend
    /// this for any new Play Mode test file instead of re-deriving the same fixes by hand.
    /// </summary>
    public abstract class TsPlayModeTestBase
    {
        protected readonly ClientSimPlayerEnvironment Players = new ClientSimPlayerEnvironment();

        private ITestVerdictSink _verdict;

        [OneTimeSetUp]
        public void TsPlayModeTestBase_OneTimeSetUp()
        {
            _verdict = new ConsoleLogVerdictSink(GetType().Name);

            foreach (var fixup in FixupRegistry.ActiveFixups)
                fixup.OnBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void TsPlayModeTestBase_OneTimeTearDown()
        {
            bool passed = _verdict.FailedNames.Count == 0;
            _verdict.Report();

            foreach (var fixup in FixupRegistry.ActiveFixups)
                fixup.OnAfterAllTests(passed);
        }

        [UnityTearDown]
        public IEnumerator TsPlayModeTestBase_UnityTearDown()
        {
            Players.Teardown();

            foreach (var fixup in FixupRegistry.ActiveFixups)
                fixup.OnUnityTearDown();

            yield return null;
        }

        protected IEnumerator StartClientSim(bool localPlayerIsMaster = true)
        {
            return Players.Start(localPlayerIsMaster);
        }

        /// <summary>Records one test's outcome. See ITestVerdictSink for why this replaces Assert as the real signal.</summary>
        protected void RecordResult(string testName, bool passed, string details)
        {
            _verdict.Record(testName, passed, details);
        }
    }
}
