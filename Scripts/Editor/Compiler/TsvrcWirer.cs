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
        private const string CompiledTypeFullName = "Tsvrc.Core.Compiled.CompiledTsvrc, Assembly-CSharp";

        static TsvrcWirer()
        {
            if (!EditorPrefs.GetBool(PendingWireKey, false)) return;
            EditorApplication.delayCall += RunWire;
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

            EnsureProgramAsset();

            var compiled = RequireCompiledTsvrc(compiledType);
            if (compiled == null) return;

            var modules = TsvrcCompiler.CreateModules();
            foreach (var module in modules)
                module.ScanForWire(config, compiledType);

            var so = new SerializedObject(compiled);
            foreach (var module in modules)
                module.Wire(so);
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[TsvrcWirer] Scene wired successfully.");
        }

        // UdonSharp requires a .asset program file to exist alongside the .cs before a component can be added.
        // Creates it if missing — mirrors what the UdonSharp script creation wizard does.
        private static void EnsureProgramAsset()
        {
            string assetPath = TsvrcCompiler.GeneratedFolder + "/CompiledTsvrc.asset";
            string scriptPath = TsvrcCompiler.GeneratedFolder + "/CompiledTsvrc.cs";

            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                return;

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (monoScript == null)
            {
                Debug.LogError($"[TsvrcWirer] CompiledTsvrc.cs not found at '{scriptPath}'.");
                return;
            }

            var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = monoScript;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            AssetDatabase.SaveAssets();
        }

        private static Component RequireCompiledTsvrc(Type compiledType)
        {
            var existing = (Component)UnityEngine.Object.FindObjectOfType(compiledType);
            if (existing != null) return existing;

            var go = new GameObject("CompiledTsvrc");
            go.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(go, "Create CompiledTsvrc");
            return UdonSharpUndo.AddComponent(go, compiledType);
        }

        private static TsvrcConfig RequireTsvrcConfig()
        {
            var all = UnityEngine.Object.FindObjectsOfType<TsvrcConfig>();
            if (all.Length == 1) return all[0];
            if (all.Length > 1)
                Debug.LogError("[TsvrcWirer] Multiple TsvrcConfig found — remove duplicates.");
            else
                Debug.LogError("[TsvrcWirer] No TsvrcConfig found in the active scene.");
            return null;
        }
    }
}
#endif
