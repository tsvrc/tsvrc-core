#if UNITY_EDITOR
using System;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// After Tsvrc/Compile rewrites the generated .cs files and Unity recompiles,
    /// creates UdonSharpProgramAssets for both generated scripts, then wires the
    /// CompiledTsvrc + CompiledTsvrcInstance scene objects.
    /// </summary>
    [InitializeOnLoad]
    public static class TsvrcSceneWirer
    {
        private const string AccessorCsPath    = "Assets/TsvrcGenerated/CompiledTsvrc.cs";
        private const string AccessorAssetPath = "Assets/TsvrcGenerated/CompiledTsvrc.asset";
        private const string InstanceCsPath    = "Assets/TsvrcGenerated/CompiledTsvrcInstance.cs";
        private const string InstanceAssetPath = "Assets/TsvrcGenerated/CompiledTsvrcInstance.asset";

        static TsvrcSceneWirer() { }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (!EditorPrefs.GetBool(TsvrcCompiler.PendingWireKey, false)) return;
            EditorPrefs.DeleteKey(TsvrcCompiler.PendingWireKey);

            // C# types exist now but UdonSharp hasn't built its program-asset lookup yet.
            // Create the .asset files here so the AssetDatabase postprocessor fires and
            // clears the cache; InitTypeLookups() will re-read them before WireScene
            // calls AddComponent, preventing the "no program asset" crash.
            EnsureProgramAsset(AccessorCsPath,  AccessorAssetPath);
            EnsureProgramAsset(InstanceCsPath,  InstanceAssetPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // triggers UdonSharpProgramAssetPostprocessor → ClearProgramAssetCache

            EditorApplication.delayCall += WireScene;
        }

        // ── Program asset creation ────────────────────────────────────────────────────────

        private static void EnsureProgramAsset(string csPath, string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                return; // already exists

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(csPath);
            if (monoScript == null)
            {
                Debug.LogError($"[TsvrcSceneWirer] MonoScript not found at '{csPath}'.");
                return;
            }

            var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = monoScript;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            Debug.Log($"[TsvrcSceneWirer] Created program asset '{assetPath}'.");
        }

        private static void WireScene()
        {
            var result = TsvrcScanner.Scan();
            if (result == null) return;

            // ── Resolve generated types ─────────────────────────────────────────────────────
            var tsType = Type.GetType("Tsvrc.Core.Compiled.CompiledTsvrc, Assembly-CSharp");
            var instType = Type.GetType("Tsvrc.Core.Compiled.CompiledTsvrcInstance, Assembly-CSharp");

            if (tsType == null)
            {
                Debug.LogError("[TsvrcSceneWirer] CompiledTsvrc type not found — did compilation succeed?");
                return;
            }
            if (instType == null)
            {
                Debug.LogError("[TsvrcSceneWirer] CompiledTsvrcInstance type not found — did compilation succeed?");
                return;
            }

            // ── CompiledTsvrc : wire [SerializeField] public singleton fields ───────────────
            var tsGo = FindOrCreateGameObject("CompiledTsvrc", tsType);
            var tsComponent = (Component)tsGo.GetComponent(tsType);

            var tsSerialized = new SerializedObject(tsComponent);
            foreach (var group in result.Groups)
                if (group.Kind == TsvrcGroupKind.Singleton)
                    foreach (var entry in group.Entries)
                        SetField(tsSerialized, entry.FieldName, ResolveComponent(entry));
            tsSerialized.ApplyModifiedProperties();

            // ── CompiledTsvrcInstance : wire _ts + behaviour template fields ────────────────
            var instGo = FindOrCreateGameObject("CompiledTsvrcInstance", instType);
            var instComponent = (Component)instGo.GetComponent(instType);

            var instSerialized = new SerializedObject(instComponent);

            // _ts → CompiledTsvrc
            SetField(instSerialized, "_ts", tsComponent);

            // FactoryUsed behaviour templates → _fieldName
            foreach (var group in result.Groups)
                if (group.Kind == TsvrcGroupKind.Behaviour)
                    foreach (var entry in group.Entries)
                        if (entry.FactoryUsed)
                            SetField(instSerialized, "_" + LowerFirst(entry.FieldName), ResolveComponent(entry));

            instSerialized.ApplyModifiedProperties();

            // ── Summary log ───────────────────────────────────────────────────────────────
            int singletonCount = 0, factoryCount = 0;
            foreach (var group in result.Groups)
            {
                if (group.Kind == TsvrcGroupKind.Singleton) singletonCount += group.Entries.Count;
                if (group.Kind == TsvrcGroupKind.Behaviour)
                    foreach (var entry in group.Entries)
                        if (entry.FactoryUsed) factoryCount++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[TsvrcSceneWirer] Wired {singletonCount} singleton(s) → CompiledTsvrc, {factoryCount} factory template(s) → CompiledTsvrcInstance.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────────

        private static GameObject FindOrCreateGameObject(string goName, Type componentType)
        {
            var found = UnityEngine.Object.FindObjectsOfType(componentType);
            if (found != null && found.Length > 0)
            {
                var existing = ((Component)found[0]).gameObject;
                existing.transform.SetSiblingIndex(0);
                return existing;
            }

            var go = new GameObject(goName);
            go.AddComponent(componentType);
            go.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(go, $"Create {goName}");
            return go;
        }

        private static UnityEngine.Object ResolveComponent(TsvrcEntry entry)
        {
            if (entry.SceneObject is GameObject go)
                return go.GetComponent(entry.Type);
            return entry.SceneObject; // already a Component reference
        }

        private static void SetField(SerializedObject so, string fieldName, UnityEngine.Object value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null) prop.objectReferenceValue = value;
            else Debug.LogWarning($"[TsvrcSceneWirer] Field '{fieldName}' not found on {so.targetObject.GetType().Name}.");
        }

        private static string LowerFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
#endif
