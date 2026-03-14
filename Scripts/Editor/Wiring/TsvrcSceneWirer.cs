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
            var result = TsvrcScannerOld.Scan();
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

            var poolGo = new GameObject("Pool");
            poolGo.transform.SetParent(tsGo.transform, false);
            Undo.RegisterCreatedObjectUndo(poolGo, "Create Pool GO");

            var tsSerialized = new SerializedObject(tsComponent);
            WireSingletonFields(tsSerialized, result, tsGo);
            WirePoolFields(tsSerialized, result, poolGo);
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

        private static void WireSingletonFields(SerializedObject so, TsvrcScanResult result, GameObject parent)
        {
            foreach (var group in result.Groups)
            {
                if (group.Kind != TsvrcGroupKind.Singleton) continue;
                foreach (var entry in group.Entries)
                {
                    if (!entry.SingletonUsed) continue;

                    if (entry.SceneObject != null && EditorUtility.IsPersistent(entry.SceneObject))
                    {
                        var sourceGo = entry.SceneObject is Component c
                            ? c.gameObject
                            : entry.SceneObject as GameObject;

                        if (sourceGo != null)
                        {
                            var instance = (GameObject)PrefabUtility.InstantiatePrefab(sourceGo, parent.transform);
                            instance.name = sourceGo.name;
                            Undo.RegisterCreatedObjectUndo(instance, "Create Singleton GO");
                            SetField(so, entry.FieldName, instance.GetComponent(entry.Type));
                            Debug.Log($"[TsvrcSceneWirer] Instantiated singleton prefab '{sourceGo.name}' under '{parent.name}'.");
                            continue;
                        }
                    }

                    SetField(so, entry.FieldName, ResolveComponent(entry));
                }
            }
        }

        private static void WirePoolFields(SerializedObject so, TsvrcScanResult result, GameObject parent)
        {
            foreach (var group in result.Groups)
            {
                if (group.Kind != TsvrcGroupKind.Pool) continue;
                foreach (var entry in group.Entries)
                {
                    if (!entry.GetterUsed) continue;
                    if (entry.SceneObject == null) continue;

                    var sourceGo = entry.SceneObject is Component c
                        ? c.gameObject
                        : entry.SceneObject as GameObject;

                    if (sourceGo == null) continue;

                    // Create one scene-placed pool slot per call site.
                    // Being in the scene at load time means VRChat assigns each slot a
                    // network ID, allowing UdonSharp network events to work on them.
                    for (int i = 0; i < entry.CallSites.Count; i++)
                    {
                        GameObject poolGo;
                        if (EditorUtility.IsPersistent(sourceGo))
                            poolGo = (GameObject)PrefabUtility.InstantiatePrefab(sourceGo, parent.transform);
                        else
                            poolGo = UnityEngine.Object.Instantiate(sourceGo, parent.transform);

                        poolGo.name = sourceGo.name + "_" + i;
                        poolGo.SetActive(false);
                        Undo.RegisterCreatedObjectUndo(poolGo, "Create Pool Slot GO");
                        string fieldName = "_" + LowerFirst(entry.FieldName) + "_" + i;
                        SetField(so, fieldName, poolGo.GetComponent(entry.Type));
                        Debug.Log($"[TsvrcSceneWirer] Created pool slot [{i}] '{poolGo.name}' for {entry.Type.Name} ({entry.CallSites[i].ClassName}).");
                    }
                }
            }
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
            int singletons = 0, pools = 0;
            foreach (var group in result.Groups)
            {
                if (group.Kind == TsvrcGroupKind.Singleton)
                    foreach (var e in group.Entries) { if (e.SingletonUsed) singletons++; }
                if (group.Kind == TsvrcGroupKind.Pool)
                    foreach (var e in group.Entries) { if (e.GetterUsed) pools++; }
            }

            string instanceInfo = instanceComponent != null
                ? result.InstanceType.Name
                : "none";

            Debug.Log(
                $"[TsvrcSceneWirer] Wired {singletons} singleton(s), " +
                $"{pools} pool slot(s), " +
                $"instance ({instanceInfo}) \u2192 CompiledTsvrc.");
        }

        private static string LowerFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
#endif
