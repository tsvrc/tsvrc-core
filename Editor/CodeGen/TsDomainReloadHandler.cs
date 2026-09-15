#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // delayCall defers until scene objects are registered; FindObjectsOfType is empty during the static constructor.
    [InitializeOnLoad]
    internal static class TsDomainReloadHandler
    {
        static TsDomainReloadHandler() => EditorApplication.delayCall += () =>
        {
            // Exactly one install must exist. A second one collides GUIDs and silently orphans
            // every existing reference. Checked first since this assembly still runs even if that breaks the build.
            if (OtherInstallPathExists(out string otherPath))
                Debug.LogError($"[Tsvrc] Both '{PackagePaths.Root}' and '{otherPath}' exist - only one " +
                    "install (.unitypackage or VPM package) may be present at a time. Having both makes " +
                    "Unity assign a fresh GUID to one copy's scripts, silently breaking every existing " +
                    $"reference to them (scenes, prefabs, TsConfig entries). Delete '{otherPath}' completely " +
                    "(folder and .meta), then let Unity reimport.");

            // Checked eagerly, before anything else below, and deliberately not gated by
            // AutomaticTriggersSuppressed: a totally missing scaffold file looks like any other
            // compile break to the compiler, so a project that lost Assets/TsGenerated entirely
            // (a bad .gitignore on a fresh clone, an accidental folder delete) just sees a wall of
            // unrelated CS1061s with no hint that the fix is Force Regenerate. This assembly never
            // depends on Assembly-CSharp, so it can still run and explain the problem even while
            // the rest of the project won't compile at all.
            if (TsLinkedScene.IsConfigured && !ScaffoldModule.ScaffoldFileExists())
                Debug.LogError("[Tsvrc] The generated scaffold file " +
                    $"('{TsPaths.GeneratedFolder}/{TsPaths.CompiledClassName}.cs') is missing even though a " +
                    $"scene is linked. If the project is failing to compile with errors mentioning " +
                    $"'{TsPaths.CompiledClassName}', this is almost certainly why - open Tsvrc > Configure " +
                    "and click \"Force Regenerate\" to restore it.");

            // TsGenerator.AutomaticTriggersSuppressed's -runTests check is the only signal
            // available at this exact moment: right after the first domain reload, before Unity
            // Test Framework has discovered or started running anything, so no test-side
            // [SetUpFixture] has run yet either. Without this, AfterDomainReload() would run for
            // real against whatever scene the NUnit batchmode test runner happens to have open, a
            // scene that never has the real project's TsConfig, and since compiling clean with a
            // smaller live result is (by design, see TsModule.ApplySnapshotFallback) treated as a
            // legitimate removal, it would overwrite the real generated Global/Factory/Pool/
            // Construct files with an almost-empty result. Tests call TsGenerator.Run()/
            // AfterDomainReload() directly and deliberately when they want to exercise it, so
            // skipping only this automatic entry point doesn't affect them.
            if (TsGenerator.AutomaticTriggersSuppressed) return;
            TsGenerator.AfterDomainReload();
        };

        private static bool OtherInstallPathExists(out string otherPath)
        {
            otherPath = PackagePaths.Root == "Assets/Tsvrc" ? "Packages/com.tsvrc.core" : "Assets/Tsvrc";
            return System.IO.File.Exists(TsPaths.ToFullPath(otherPath) + "/package.json");
        }
    }
}
#endif
