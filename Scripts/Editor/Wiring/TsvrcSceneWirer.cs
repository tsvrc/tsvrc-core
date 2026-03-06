#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Wires the generated scene hierarchy after compile.
    // Hierarchy: CompiledTsvrcConfig (root) > CompiledTsvrc + [InstanceGO]
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
            var instType = ResolveType("Tsvrc.Core.Compiled.CompiledTsvrcConfig", "CompiledTsvrcConfig");
            if (tsType == null || instType == null) return;

            foreach (var obj in UnityEngine.Object.FindObjectsOfType(instType))
                Undo.DestroyObjectImmediate(((Component)obj).gameObject);

            var instGo = new GameObject("CompiledTsvrcConfig");
            instGo.AddComponent(instType);
            instGo.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(instGo, "Create CompiledTsvrcConfig");
            var instComponent = (Component)instGo.GetComponent(instType);

            var tsGo = new GameObject("CompiledTsvrc");
            tsGo.AddComponent(tsType);
            tsGo.transform.SetParent(instGo.transform, false);
            Undo.RegisterCreatedObjectUndo(tsGo, "Create CompiledTsvrc");
            var tsComponent = (Component)tsGo.GetComponent(tsType);

            var tsSerialized = new SerializedObject(tsComponent);
            WireSingletonFields(tsSerialized, result);
            WireBehaviourTemplateFields(tsSerialized, result);
            tsSerialized.ApplyModifiedProperties();

            var instanceComponent = CreateInstanceGo(result, instGo);

            var instSerialized = new SerializedObject(instComponent);
            SetField(instSerialized, "_ts", tsComponent);
            if (instanceComponent != null)
                SetField(instSerialized, "_instance", instanceComponent);
            WireConstructFields(instSerialized, result);
            instSerialized.ApplyModifiedProperties();

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
                        if (entry.FactoryUsed)
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
                $"[TsvrcSceneWirer] Wired {singletons} singleton(s) \u2192 CompiledTsvrc, " +
                $"{factories} factory template(s) \u2192 CompiledTsvrc, " +
                $"instance ({instanceInfo}) copied \u2192 CompiledTsvrcConfig.");
        }

        private static string LowerFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
#endif
