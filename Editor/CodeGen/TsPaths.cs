#if UNITY_EDITOR
using System.IO;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Single source of truth for every path and identity TsGenerator writes into or looks up:
    // the folder generated files land in, the compiled scaffold type's name and namespace, and
    // optionally an explicit override for the scaffold's own script and program asset paths.
    // These are mutable fields rather than consts so tests can redirect them to scratch
    // locations or a test owned double type for the duration of a test, then restore the
    // defaults afterward. TempSceneScope.Dispose always calls ResetToDefaults, so every test
    // that uses a temp scene gets this safety net for free, whether or not it explicitly
    // touches TsPaths itself. Production code never changes these; this whole file is a
    // test only seam.
    internal static class TsPaths
    {
        internal const string DefaultGeneratedFolder = "Assets/TsGenerated";
        internal const string DefaultCompiledNamespace = "Tsvrc.Core.Generated";
        internal const string DefaultCompiledClassName = "TsGenerated";

        internal static string GeneratedFolder = DefaultGeneratedFolder;
        internal static string CompiledNamespace = DefaultCompiledNamespace;
        internal static string CompiledClassName = DefaultCompiledClassName;

        // Null means derive the path from GeneratedFolder and CompiledClassName, which is what
        // ScaffoldModule normally does. Only ever set explicitly by tests that need the scaffold
        // script itself to live somewhere other than {GeneratedFolder}/{CompiledClassName}.cs,
        // for example pointing at a permanent, already compiled test double instead of a
        // freshly written scratch file that hasn't compiled yet.
        internal static string ScaffoldScriptPath;
        internal static string ScaffoldAssetPath;

        // Null means use the real UnityEditor.EditorUtility.scriptCompilationFailed. That
        // property has no public setter, so there is no other way to exercise
        // TsModule.ApplySnapshotFallback's gating from a test without an actual broken compile,
        // which would break the whole test assembly. Tests set this directly instead.
        internal static bool? ScriptCompilationFailedOverride;

        internal static bool ScriptCompilationFailed =>
            ScriptCompilationFailedOverride ?? UnityEditor.EditorUtility.scriptCompilationFailed;

        internal static void ResetToDefaults()
        {
            GeneratedFolder = DefaultGeneratedFolder;
            CompiledNamespace = DefaultCompiledNamespace;
            CompiledClassName = DefaultCompiledClassName;
            ScaffoldScriptPath = null;
            ScaffoldAssetPath = null;
            ScriptCompilationFailedOverride = null;
        }

        // Turns a project-relative asset path ("Assets/TsGenerated/Foo.cs") into an absolute
        // filesystem path, for the handful of call sites that need raw File I/O rather than
        // AssetDatabase (TsGenerator's writes, ModuleEntrySnapshot's cache, TsWindow's file
        // existence checks). Single source of truth instead of each duplicating this logic.
        internal static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
#endif
