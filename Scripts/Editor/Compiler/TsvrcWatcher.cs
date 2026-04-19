#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Watches for asset changes that would invalidate the compiled output.
    ///
    /// Three response levels:
    ///   FullCompile — regenerates CompiledTsvrc.cs, triggers domain reload, then wires.
    ///                 Required when call sites or baked data may have changed.
    ///   WireOnly    — skips code gen and domain reload; just re-runs the wire pass.
    ///                 Safe when only scene-reference assets changed (e.g. prefab content).
    ///   None        — no relevant change detected.
    ///
    /// Tracked assets are collected from every <see cref="TsvrcModule"/> via
    /// <see cref="TsvrcModule.GetTrackedAssetPaths"/>. The TsvrcConfig asset path always forces
    /// a FullCompile. Any .cs file change under Assets/ also forces a FullCompile because
    /// call-site scanning depends on user code.
    /// </summary>
    internal class TsvrcWatcher : AssetPostprocessor
    {
        internal const string NeedsCompileKey = "Tsvrc.NeedsCompile";

        private enum CompileScope { None, WireOnly, FullCompile }

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

            // Non-.cs changes don't trigger a domain reload, so schedule work directly.
            switch (GetRequiredScope(changed))
            {
                case CompileScope.FullCompile:
                    EditorApplication.delayCall += () =>
                    {
                        EditorPrefs.DeleteKey(NeedsCompileKey);
                        TsvrcCompiler.Compile();
                    };
                    break;

                case CompileScope.WireOnly:
                    // No code change — skip code gen and domain reload, just re-wire.
                    EditorApplication.delayCall += TsvrcWirer.WireNow;
                    break;
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

        private static CompileScope GetRequiredScope(HashSet<string> changed)
        {
            if (changed.Count == 0) return CompileScope.None;

            // TsvrcConfig structure changes always require full code regeneration.
            if (changed.Contains(TsvrcCompiler.TsvrcConfigPrefabPath))
                return CompileScope.FullCompile;

            var scope = CompileScope.None;
            foreach (var module in TsvrcCompiler.CreateModules())
            {
                foreach (var path in module.GetFullCompileAssetPaths())
                    if (changed.Contains(path)) return CompileScope.FullCompile;

                if (scope < CompileScope.WireOnly)
                    foreach (var path in module.GetWireOnlyAssetPaths())
                        if (changed.Contains(path)) { scope = CompileScope.WireOnly; break; }
            }

            return scope;
        }
    }
}
#endif
