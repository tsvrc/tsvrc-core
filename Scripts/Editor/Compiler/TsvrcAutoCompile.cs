#if UNITY_EDITOR
using UnityEditor;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Automatically re-compiles Tsvrc after any domain reload where <see cref="TsvrcWatcher"/>
    /// detected a relevant asset change.
    ///
    /// Flow:
    ///   1. User edits a prefab / translation JSON / .cs file.
    ///   2. <see cref="TsvrcWatcher.OnPostprocessAllAssets"/> sets <c>Tsvrc.NeedsCompile = true</c>.
    ///   3. Unity triggers a domain reload (script changes) or just completes the import.
    ///      For non-.cs asset changes no domain reload occurs — the flag persists in EditorPrefs
    ///      until the next reload (e.g. triggered by UdonSharp when the user saves a script).
    ///   4. This class static constructor runs, reads the flag, clears it, and schedules
    ///      <see cref="TsvrcCompiler.Compile"/> via <c>EditorApplication.delayCall</c> so the
    ///      asset database is fully settled before we touch it.
    /// </summary>
    [InitializeOnLoad]
    internal static class TsvrcAutoCompile
    {
        static TsvrcAutoCompile()
        {
            if (!EditorPrefs.GetBool(TsvrcWatcher.NeedsCompileKey, false)) return;

            // Clear the flag inside the lambda so it is only removed once the compile actually runs.
            // Clearing it before scheduling would lose the flag if the Editor exits before delayCall fires.
            EditorApplication.delayCall += () =>
            {
                EditorPrefs.DeleteKey(TsvrcWatcher.NeedsCompileKey);
                TsvrcCompiler.Compile();
            };
        }
    }
}
#endif
