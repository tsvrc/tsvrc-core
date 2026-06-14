#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace Tsvrc.Editor
{
    // Watches for asset changes that would invalidate the compiled output and triggers a recompile.
    //
    // Two triggers:
    //   1. Data assets (e.g. translation JSON files): cheap path-set check via GetFullCompileAssetPaths().
    //   2. User .cs files: dry-run scan+codegen to detect added or removed call sites
    //      (e.g. a new _ts.GetFoo() call). Domain reload only fires if the output would actually change.
    internal class TsvrcWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            // Auto-recompile disabled: V1 compiler is pending removal.
            // Use Tsvrc > Tools > Force Compile to compile manually.
        }

        private static bool RequiresFullCompile(HashSet<string> changed)
        {
            if (changed.Count == 0) return false;
            foreach (var module in TsvrcCompiler.CreateModules())
                foreach (var path in module.GetFullCompileAssetPaths())
                    if (changed.Contains(path)) return true;
            return false;
        }

        // Runs a dry-run compile when any user .cs file changes to detect new or removed
        // call sites (e.g. _ts.GetFoo()). The scan cost (~50-200ms) is paid on every .cs save,
        // but the domain reload (~5s) is only triggered when the output would actually change.
        //
        // Skip files under GeneratedFolder: after Compile() calls AssetDatabase.Refresh(), Unity
        // re-imports CompiledTsvrc.cs with didDomainReload=false, which would otherwise trigger
        // a pointless scan before the real domain reload arrives.
        private static readonly string GeneratedFolderPrefix = TsvrcCompiler.GeneratedFolder + "/";

        private static bool RequiresSourceCompile(HashSet<string> changed)
        {
            foreach (var p in changed)
                if (p.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase)
                    && !p.StartsWith(GeneratedFolderPrefix, System.StringComparison.Ordinal))
                    return TsvrcCompiler.WouldChangeSource();
            return false;
        }
    }
}
#endif
