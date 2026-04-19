#if UNITY_EDITOR
using System;
using Tsvrc.Core;
using UdonSharp;
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
            // Compile must run before wire: if both are pending, compile will re-schedule the wire.
            if (EditorPrefs.GetBool(PendingCompileKey, false))
            {
                EditorPrefs.DeleteKey(PendingCompileKey);
                EditorApplication.delayCall += () => TsvrcCompiler.Compile();
                return;
            }
            if (EditorPrefs.GetBool(PendingWireKey, false))
                EditorApplication.delayCall += RunWire;
        }

        /// <summary>
        /// Persists a pending compile across a domain reload.
        /// Call this instead of scheduling a delayCall whenever a .cs file change triggers a recompile,
        /// since delayCall delegates are wiped when Unity reloads the domain for the .cs change.
        /// </summary>
        internal static void ScheduleCompile()
        {
            EditorPrefs.SetBool(PendingCompileKey, true);
        }

        /// <summary>Called by TsvrcCompiler after writing the generated file, before AssetDatabase.Refresh().</summary>
        internal static void ScheduleWire()
        {
            EditorPrefs.SetBool(PendingWireKey, true);
        }

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
        }

        // UdonSharp requires a .asset program file to exist alongside the .cs before a component can be added.
        // Creates it if missing, mirroring what the UdonSharp script creation wizard does.
        // Returns false if the script could not be found, RunWire must not continue in that case.
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
