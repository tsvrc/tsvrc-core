#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Wires the generated scene hierarchy after compile.
    // Hierarchy: CompiledTsvrc (root) > [InstanceGO]
    [InitializeOnLoad]
    public static class TsvrcSceneWirer
    {
        static TsvrcSceneWirer() { }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (!EditorPrefs.GetBool(TsvrcCompiler.PendingWireKey, false)) return;
            EditorPrefs.DeleteKey(TsvrcCompiler.PendingWireKey);

            TsvrcProgramAssets.EnsureBothAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorApplication.delayCall += WireScene;
        }

        internal static void WireScene()
        {
            var result = TsvrcScanner.Scan();
            if (result == null) return;

            TsvrcProgramAssets.EnsureBothAssets();

            var tsType = ResolveType("Tsvrc.Core.Compiled.CompiledTsvrc", "CompiledTsvrc");
            if (tsType == null) return;

            // Destroy existing CompiledTsvrc GOs.
            foreach (var obj in UnityEngine.Object.FindObjectsOfType(tsType))
                Undo.DestroyObjectImmediate(((Component)obj).gameObject);

            // Clean up legacy CompiledTsvrcConfig root GO (one-time transition).
            var oldConfigGo = GameObject.Find("CompiledTsvrcConfig");
            if (oldConfigGo != null)
                Undo.DestroyObjectImmediate(oldConfigGo);

            var tsGo = new GameObject("CompiledTsvrc");
            tsGo.AddComponent(tsType);
            tsGo.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(tsGo, "Create CompiledTsvrc");
            var tsComponent = (Component)tsGo.GetComponent(tsType);

            var tsSerialized = new SerializedObject(tsComponent);
            WireSingletonFields(tsSerialized, result);
            WireBehaviourTemplateFields(tsSerialized, result);
            WireConstructFields(tsSerialized, result);

            var instanceComponent = CreateInstanceGo(result, tsGo);
            if (instanceComponent != null)
                SetField(tsSerialized, "_instance", instanceComponent);

            tsSerialized.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            LogSummary(result, instanceComponent);
        }


        private static Type ResolveType(string fullName, string label)
        {
            var type = Type.GetType(fullName + ", Assembly-CSharp");
            if (type == null)
                Debug.LogError($"[TsvrcSceneWirer] {label} type not found \u2014 did compilation succeed?");
            return type;
        }

        private static void WireSingletonFields(SerializedObject so, TsvrcScanResult result)
        {
            foreach (var group in result.Groups)
                if (group.Kind == TsvrcGroupKind.Singleton)
                    foreach (var entry in group.Entries)
                        if (entry.SingletonUsed)
                            SetField(so, entry.FieldName, ResolveComponent(entry));
        }

        private static void WireBehaviourTemplateFields(SerializedObject so, TsvrcScanResult result)
        {
            foreach (var group in result.Groups)
                if (group.Kind == TsvrcGroupKind.Behaviour)
                    foreach (var entry in group.Entries)
                        if (entry.FactoryUsed || entry.IsCore)
                            SetField(so, "_" + LowerFirst(entry.FieldName), ResolveComponent(entry));
        }

        private static void WireConstructFields(SerializedObject so, TsvrcScanResult result)
        {
            foreach (var group in result.Groups)
                if (group.Kind == TsvrcGroupKind.Construct)
                    foreach (var entry in group.Entries)
                        SetField(so, "_" + LowerFirst(entry.FieldName), ResolveComponent(entry));
        }

        private static Component CreateInstanceGo(TsvrcScanResult result, GameObject parent)
        {
            if (result.InstanceType == null)
                return null; // no TsvrcInstance subclass detected — omit instance GO

            TsvrcProgramAssets.EnsureAssetForType(result.InstanceType);

            var instanceGo = new GameObject(result.InstanceType.Name);
            instanceGo.AddComponent(result.InstanceType);
            instanceGo.transform.SetParent(parent.transform, false);
            Undo.RegisterCreatedObjectUndo(instanceGo, "Create Instance GO");

            Debug.Log($"[TsvrcSceneWirer] Created instance GO '{result.InstanceType.Name}' (auto-detected).");
            return (Component)instanceGo.GetComponent(result.InstanceType);
        }

        private static UnityEngine.Object ResolveComponent(TsvrcEntry entry)
        {
            if (entry.SceneObject is GameObject go)
                return go.GetComponent(entry.Type);
            return entry.SceneObject;
        }

        private static void SetField(SerializedObject so, string fieldName, UnityEngine.Object value)
        {
            var prop = so.FindProperty(fieldName);
            if (prop != null)
                prop.objectReferenceValue = value;
            else
                Debug.LogWarning($"[TsvrcSceneWirer] Field '{fieldName}' not found on {so.targetObject.GetType().Name}.");
        }

        private static void LogSummary(TsvrcScanResult result, Component instanceComponent)
        {
            int singletons = 0, factories = 0;
            foreach (var group in result.Groups)
            {
                if (group.Kind == TsvrcGroupKind.Singleton)
                    foreach (var e in group.Entries) { if (e.SingletonUsed) singletons++; }
                if (group.Kind == TsvrcGroupKind.Behaviour)
                    foreach (var e in group.Entries) { if (e.FactoryUsed) factories++; }
            }

            string instanceInfo = instanceComponent != null
                ? result.InstanceType.Name
                : "none";

            Debug.Log(
                $"[TsvrcSceneWirer] Wired {singletons} singleton(s), " +
                $"{factories} factory template(s), " +
                $"instance ({instanceInfo}) \u2192 CompiledTsvrc.");
        }

        private static string LowerFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
#endif
