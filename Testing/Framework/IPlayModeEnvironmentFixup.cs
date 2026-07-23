namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// One known VRCSDK/Unity Play Mode testing defect and its fix, applied automatically
    /// by TsPlayModeTestBase at the matching lifecycle point. Add a new file here (with a
    /// proof test confirming it does what it claims) whenever a new quirk is found, instead
    /// of hand-patching every affected test file.
    /// </summary>
    public interface IPlayModeEnvironmentFixup
    {
        /// <summary>Runs once, in [OneTimeSetUp], before any test in the file executes.</summary>
        void OnBeforeAnyTests();

        /// <summary>Runs at the start of every [UnityTearDown], after ClientSim teardown.</summary>
        void OnUnityTearDown();

        /// <summary>Runs once in [OneTimeTearDown], after the final pass/fail verdict is known.</summary>
        void OnAfterAllTests(bool allPassed);
    }
}
