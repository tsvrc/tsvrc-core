#if UNITY_EDITOR
using UnityEditor;

namespace Tsvrc.Editor
{
    // delayCall defers until scene objects are registered; FindObjectsOfType is empty during the static constructor.
    [InitializeOnLoad]
    internal static class TsDomainReloadHandler
    {
        static TsDomainReloadHandler() => EditorApplication.delayCall += () =>
        {
            // TsGenerator.AutomaticTriggersSuppressed's -runTests check is the only signal
            // available at this exact moment: right after the first domain reload, before Unity
            // Test Framework has discovered or started running anything, so no test-side
            // [SetUpFixture] has run yet either. Without this, AfterDomainReload() would run for
            // real against whatever scene the NUnit batchmode test runner happens to have open, a
            // scene that never has the real project's TsConfig, and since compiling clean with a
            // smaller live result is (by design, see TsModule.ApplySnapshotFallback) treated as a
            // legitimate removal, it would overwrite the real generated Singleton/Factory/Pool/
            // Construct files with an almost-empty result. Tests call TsGenerator.Run()/
            // AfterDomainReload() directly and deliberately when they want to exercise it, so
            // skipping only this automatic entry point doesn't affect them.
            if (TsGenerator.AutomaticTriggersSuppressed) return;
            TsGenerator.AfterDomainReload();
        };
    }
}
#endif
