#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Watches for asset changes that would invalidate the compiled output and schedules a
    /// recompile by setting a persistent flag that <see cref="TsvrcAutoCompile"/> reads after
    /// the next domain reload.
    ///
    /// Tracked assets are collected from every <see cref="TsvrcModule"/> via
    /// <see cref="TsvrcModule.GetTrackedAssetPaths"/>. The TsvrcConfig asset path is always tracked.
    /// Any .cs file change under Assets/ is tracked because call-site scanning depends on user code.
    /// </summary>
    internal class TsvrcWatcher : AssetPostprocessor
    {
        internal const string NeedsCompileKey = "Tsvrc.NeedsCompile";

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            // After a domain reload the flag is read by TsvrcAutoCompile — don't set it again here
            // based on that same event, otherwise every reload triggers a compile unconditionally.
            if (didDomainReload) return;

            // Check cheap .cs condition first — avoids allocating the HashSet on unrelated imports
            // (e.g. texture saves) where we can already answer with a simple loop.
            if (AnyUserScriptChanged(importedAssets) ||
                AnyUserScriptChanged(deletedAssets) ||
                AnyUserScriptChanged(movedAssets))
            {
                EditorPrefs.SetBool(NeedsCompileKey, true);
                return;
            }

            // For non-.cs assets build a set once and check against tracked paths.
            var changed = new HashSet<string>(importedAssets);
            foreach (var p in deletedAssets) changed.Add(p);
            foreach (var p in movedAssets) changed.Add(p);
            foreach (var p in movedFromAssetPaths) changed.Add(p);

            if (AnyTrackedAssetChanged(changed))
            {
                // Non-.cs changes don't trigger a domain reload, so TsvrcAutoCompile won't run.
                // Schedule the compile directly on this editor tick instead.
                EditorApplication.delayCall += () =>
                {
                    EditorPrefs.DeleteKey(NeedsCompileKey);
                    TsvrcCompiler.Compile();
                };
            }
        }

        private static bool AnyUserScriptChanged(string[] paths)
        {
            foreach (var path in paths)
                if (path.EndsWith(".cs") && path.StartsWith("Assets/") &&
                    !path.StartsWith("Assets/Tsvrc/") && !path.StartsWith("Assets/CompiledTsvrc/"))
                    return true;
            return false;
        }

        private static bool AnyTrackedAssetChanged(HashSet<string> changed)
        {
            if (changed.Count == 0) return false;

            if (changed.Contains(TsvrcCompiler.TsvrcConfigPrefabPath))
                return true;

            // Assets tracked by individual modules (prefabs, translation JSON, etc.)
            var modules = TsvrcCompiler.CreateModules();
            foreach (var module in modules)
                foreach (var trackedPath in module.GetTrackedAssetPaths())
                    if (changed.Contains(trackedPath))
                        return true;

            return false;
        }
    }
}
#endif
