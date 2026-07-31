#if UNITY_EDITOR
using System;
using UnityEditor;

namespace Tsvrc.Editor
{
    // delayCall defers until scene objects are registered; FindObjectsOfType is empty during the static constructor.
    [InitializeOnLoad]
    internal static class TsDomainReloadHandler
    {
        static TsDomainReloadHandler() => EditorApplication.delayCall += () =>
        {
            if (IsRunningAutomatedTests(Environment.GetCommandLineArgs())) return;
            TsGenerator.AfterDomainReload();
        };

        // HasBootstrapSignal() checks for a loaded Instance subclass in the AppDomain, not
        // scene content, so it stays true for a consuming project's real world scripts no
        // matter which scene happens to be active. That means this automatic trigger would
        // otherwise run for real against whatever scene the NUnit batchmode test runner opens
        // at startup, a scene that never has the real project's TsConfig, and since compiling
        // clean with a smaller live result is (by design, see ApplySnapshotFallback) treated as
        // a legitimate removal, it would overwrite the real generated Singleton/Factory/Pool/
        // Construct files with an almost-empty result. Tests call TsGenerator.Run()/
        // AfterDomainReload() directly and deliberately when they want to exercise it, so
        // skipping only this automatic entry point during a test run doesn't affect them.
        internal static bool IsRunningAutomatedTests(string[] commandLineArgs)
        {
            foreach (var arg in commandLineArgs)
                if (string.Equals(arg, "-runTests", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
#endif
