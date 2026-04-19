#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Watches for data-asset changes that would invalidate the compiled output.
    /// Only fires a FullCompile — currently triggered by translation JSON changes.
    /// .cs changes are intentionally ignored: TsvrcBuildCompile guarantees correctness before
    /// every VRChat upload. Config changes via Tsvrc > Configure are compiled on demand.
    /// </summary>
    internal class TsvrcWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            // Skip events fired because Tsvrc itself triggered a domain reload.
            if (didDomainReload) return;

            var changed = new HashSet<string>(importedAssets);
            foreach (var p in deletedAssets) changed.Add(p);
            foreach (var p in movedAssets) changed.Add(p);
            foreach (var p in movedFromAssetPaths) changed.Add(p);

            if (RequiresFullCompile(changed))
                EditorApplication.delayCall += () => TsvrcCompiler.Compile();
        }

        private static bool RequiresFullCompile(HashSet<string> changed)
        {
            if (changed.Count == 0) return false;
            foreach (var module in TsvrcCompiler.CreateModules())
                foreach (var path in module.GetFullCompileAssetPaths())
                    if (changed.Contains(path)) return true;
            return false;
        }
    }
}
#endif
