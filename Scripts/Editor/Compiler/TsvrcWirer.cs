#if UNITY_EDITOR
using System;
using Tsvrc.Core;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor
{
    [InitializeOnLoad]
    internal static class TsvrcWirer
    {
        private const string PendingWireKey = "Tsvrc.PendingWire";
        private const string PendingCompileKey = "Tsvrc.PendingCompile";
        private const string CompiledTypeFullName = "Tsvrc.Core.Compiled.CompiledTsvrc, Assembly-CSharp";

        static TsvrcWirer()
        {
            // Compile must run before wire. If both are pending, compile will reschedule the wire.
            if (EditorPrefs.GetBool(PendingCompileKey, false))
            {
                EditorPrefs.DeleteKey(PendingCompileKey);
                EditorApplication.delayCall += () => TsvrcCompiler.Compile();
                return;
            }
            if (EditorPrefs.GetBool(PendingWireKey, false))
                EditorApplication.delayCall += RunWire;
        }

        // Persists a pending compile so it survives the domain reload that follows a .cs file change.
        // A delayCall would be lost when Unity reloads the domain, so EditorPrefs is used instead.
        internal static void ScheduleCompile()
        {
            EditorPrefs.SetBool(PendingCompileKey, true);
        }

        // Called by TsvrcCompiler after writing the generated file, before AssetDatabase.Refresh().
        internal static void ScheduleWire()
        {
            EditorPrefs.SetBool(PendingWireKey, true);
        }

        // True while RunWire is queued but has not yet executed.
        internal static bool IsWirePending() => EditorPrefs.GetBool(PendingWireKey, false);

        private static void RunWire()
        {
            EditorPrefs.DeleteKey(PendingWireKey);

            var config = RequireTsvrcConfig();
            if (config == null) return;

            var compiledType = Type.GetType(CompiledTypeFullName);
            if (compiledType == null)
            {
                Debug.LogError("[TsvrcWirer] CompiledTsvrc type not found. Was the file compiled successfully?");
                return;
            }

            if (!EnsureProgramAsset()) return;

            var compiled = RequireCompiledTsvrc(compiledType);
            if (compiled == null) return;

            var modules = TsvrcCompiler.CreateModules();
            foreach (var module in modules)
                module.ScanForWire(config, compiledType);

            var so = new SerializedObject(compiled);
            foreach (var module in modules)
                module.Wire(so);
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            TsvrcCompiler.LogSuccess();

            // A source file saved during the Compile -> Refresh -> domain reload cycle arrives
            // with didDomainReload=true, which TsvrcWatcher skips. Check here so it is not lost.
            if (TsvrcCompiler.WouldChangeSource())
                EditorApplication.delayCall += () => TsvrcCompiler.Compile();
        }

        // UdonSharp requires a .asset program file alongside the .cs before a component can be added.
        // Creates it if missing. Returns false if the script itself is not found.
        private static bool EnsureProgramAsset()
        {
            string scriptPath = TsvrcCompiler.GeneratedFilePath;
            string assetPath = TsvrcCompiler.GeneratedAssetPath;

            if (TsvrcCompiler.EnsureUdonSharpProgramAsset(scriptPath, assetPath))
                return true;

            Debug.LogError($"[TsvrcWirer] CompiledTsvrc.cs not found at '{scriptPath}'. Run Tsvrc > Tools > Force Compile first.");
            return false;
        }

        private static Component RequireCompiledTsvrc(Type compiledType)
        {
            var existing = (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
            if (existing != null) return existing;

            var go = new GameObject("CompiledTsvrc");
            go.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(go, "Create CompiledTsvrc");
            return UdonSharpUndo.AddComponent(go, compiledType);
        }

        private static TsvrcConfig RequireTsvrcConfig()
        {
            var all = UnityEngine.Object.FindObjectsOfType<TsvrcConfig>(true);
            if (all.Length == 1) return all[0];
            if (all.Length > 1)
                Debug.LogError("[TsvrcWirer] Multiple TsvrcConfig found. Remove duplicates.");
            else
                Debug.LogError("[TsvrcWirer] No TsvrcConfig found in the active scene.");
            return null;
        }
    }
}
#endif
