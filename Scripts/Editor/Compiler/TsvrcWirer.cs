#if UNITY_EDITOR
using System;
using Tsvrc.Core;
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

            var compiled = (Component)UnityEngine.Object.FindObjectOfType(compiledType);
            if (compiled == null)
            {
                Debug.LogError("[TsvrcWirer] No CompiledTsvrc component found in the active scene.");
                return;
            }

            var modules = TsvrcCompiler.CreateModules();
            foreach (var module in modules)
                module.Scan(config);

            var so = new SerializedObject(compiled);
            foreach (var module in modules)
                module.Wire(so);
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[TsvrcWirer] Scene wired successfully.");
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
