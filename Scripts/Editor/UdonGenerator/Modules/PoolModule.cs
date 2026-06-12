#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tsvrc.Core;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Editor.V2
{
    internal class PoolModule : TsvrcModule
    {
        private const string CacheFile = "pool-cache.json";

        private List<PoolField> _currentFields = new List<PoolField>();

        internal override string FileName => "Pool.cs";
        internal override string StartMethodCall => _currentFields.Count > 0 ? "TsInitPool()" : null;

        internal override void OnDomainReloaded()
        {
            var previous = LoadCache();
            _currentFields = DetectWirePoolFields();
            LogDelta(previous, _currentFields);
            SaveCache(_currentFields);
        }

        internal override string GenerateCode()
        {
            if (_currentFields.Count == 0) return string.Empty;

            var slotsByType = new Dictionary<string, (string Namespace, int Count)>(StringComparer.Ordinal);
            foreach (var f in _currentFields)
            {
                slotsByType.TryGetValue(f.FieldTypeName, out var entry);
                slotsByType[f.FieldTypeName] = (f.FieldTypeNamespace, entry.Count + 1);
            }

            var usings = new List<string> { "UnityEngine" };
            foreach (var (ns, _) in slotsByType.Values)
                if (!string.IsNullOrEmpty(ns) && !usings.Contains(ns))
                    usings.Add(ns);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                foreach (var kvp in slotsByType.OrderBy(x => x.Key))
                    for (int i = 0; i < kvp.Value.Count; i++)
                        w.Line($"[SerializeField] private {kvp.Key} {SlotFieldName(kvp.Key, i)};");

                using (w.Method("private void TsInitPool()")) { }
            }

            return w.ToString();
        }

        internal override void Wire(SerializedObject target)
        {
            var compiled = target.targetObject as Component;
            if (compiled == null) return;

            var existingContainer = compiled.transform.Find("Pool");
            if (existingContainer != null)
                Undo.DestroyObjectImmediate(existingContainer.gameObject);

            var config = AssetDatabase.LoadAssetAtPath<TsvrcConfig2>(ScaffoldModule.ConfigPath);
            var poolEntries = ResolvePoolEntries(config);
            if (poolEntries.Count == 0) return;

            var slotCountByType = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in _currentFields)
            {
                slotCountByType.TryGetValue(f.FieldTypeName, out int c);
                slotCountByType[f.FieldTypeName] = c + 1;
            }

            var wireTargetsByType = CollectWireTargetsByType(compiled.gameObject.scene);
            GameObject poolContainer = null;

            foreach (var (prefabComponent, typeName) in poolEntries)
            {
                if (!slotCountByType.TryGetValue(typeName, out int slotCount) || slotCount == 0)
                    continue;

                wireTargetsByType.TryGetValue(typeName, out var targets);
                var sourceType = prefabComponent.GetType();

                for (int i = 0; i < slotCount; i++)
                {
                    if (poolContainer == null)
                    {
                        poolContainer = new GameObject("Pool");
                        Undo.RegisterCreatedObjectUndo(poolContainer, "Create Pool Container");
                        poolContainer.transform.SetParent(compiled.transform, false);
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabComponent.gameObject, poolContainer.transform);
                    if (instance == null)
                    {
                        Debug.LogWarning($"[PoolModule] Failed to instantiate prefab '{typeName}' (slot {i}).");
                        continue;
                    }

                    instance.name = $"{typeName}_{i}";
                    Undo.RegisterCreatedObjectUndo(instance, $"Create {typeName} pool slot {i}");

                    var instanceComponent = instance.GetComponent(sourceType);
                    if (instanceComponent == null)
                    {
                        Debug.LogWarning($"[PoolModule] Instance '{instance.name}' is missing component '{sourceType.Name}'. Skipping slot {i}.");
                        Undo.DestroyObjectImmediate(instance);
                        continue;
                    }

                    var initProp = target.FindProperty(SlotFieldName(typeName, i));
                    if (initProp != null)
                        initProp.objectReferenceValue = instanceComponent;
                    else
                        Debug.LogWarning($"[PoolModule] Field '{SlotFieldName(typeName, i)}' not found on TsvrcGenerated. Force compile to regenerate.");

                    if (targets != null && i < targets.Count)
                    {
                        var (behaviour, fieldName) = targets[i];
                        var behaviourSo = new SerializedObject(behaviour);
                        var prop = behaviourSo.FindProperty(fieldName);
                        if (prop != null)
                        {
                            prop.objectReferenceValue = instanceComponent;
                            behaviourSo.ApplyModifiedProperties();
                        }
                        else
                            Debug.LogWarning($"[PoolModule] '{behaviour.GetType().Name}.{fieldName}' has [WirePool] but is not serialized. Make it public or add [SerializeField].");
                    }
                }

                int targetCount = targets?.Count ?? 0;
                if (targetCount < slotCount)
                    Debug.LogWarning($"[PoolModule] '{typeName}': {slotCount} slot(s), {targetCount} [WirePool] target(s) — {slotCount - targetCount} slot(s) unassigned.");
                else if (targetCount > slotCount)
                    Debug.LogWarning($"[PoolModule] '{typeName}': {targetCount} [WirePool] target(s), {slotCount} slot(s) — {targetCount - slotCount} component(s) will keep stale references.");
            }
        }

        private static string SlotFieldName(string typeName, int index) => $"_pool_{typeName}_{index}";


        private static List<PoolField> DetectWirePoolFields()
        {
            var found = new List<PoolField>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var type in types)
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (IsWirePoolField(field))
                            found.Add(new PoolField
                            {
                                TypeName = type.FullName,
                                FieldName = field.Name,
                                FieldTypeName = field.FieldType.Name,
                                FieldTypeNamespace = field.FieldType.Namespace ?? string.Empty,
                            });
            }
            return found;
        }

        private static bool IsWirePoolField(FieldInfo field)
        {
            if (!field.GetCustomAttributes(false).Any(a => a.GetType().Name == "WirePoolAttribute"))
                return false;
            return field.IsPublic
                || field.GetCustomAttributes(false).Any(a => a.GetType().Name == "SerializeField");
        }


        private static List<(Component, string)> ResolvePoolEntries(TsvrcConfig2 config)
        {
            var result = new List<(Component, string)>();
            if (config?.PooledObjects == null) return result;

            foreach (var obj in config.PooledObjects)
            {
                if (obj == null) continue;
                if (!EditorUtility.IsPersistent(obj))
                {
                    Debug.LogWarning($"[PoolModule] '{obj.name}' is a scene object. Pool entries must be prefab assets. Skipping.");
                    continue;
                }

                var component = obj as UdonSharpBehaviour
                    ?? (obj as GameObject)?.GetComponent<UdonSharpBehaviour>();
                if (component == null)
                {
                    Debug.LogWarning($"[PoolModule] '{obj.name}' has no UdonSharpBehaviour. Skipping.");
                    continue;
                }

                result.Add((component, component.GetType().Name));
            }

            return result;
        }

        private static Dictionary<string, List<(MonoBehaviour, string)>> CollectWireTargetsByType(Scene scene)
        {
            var result = new Dictionary<string, List<(MonoBehaviour, string)>>(StringComparer.Ordinal);

            foreach (var rootGo in scene.GetRootGameObjects())
            {
                foreach (var behaviour in rootGo.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    for (var t = behaviour.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(UdonSharpBehaviour); t = t.BaseType)
                    {
                        foreach (var field in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            if (!field.GetCustomAttributes(false).Any(a => a.GetType().Name == "WirePoolAttribute")) continue;
                            if (field.FieldType.IsArray || field.FieldType.IsGenericType) continue;

                            string typeName = field.FieldType.Name;
                            if (!result.TryGetValue(typeName, out var list))
                                result[typeName] = list = new List<(MonoBehaviour, string)>();
                            list.Add((behaviour, field.Name));
                        }
                    }
                }
            }

            return result;
        }


        private static List<PoolField> LoadCache()
        {
            string json = TsvrcGenerator.ReadCacheFile(CacheFile);
            if (string.IsNullOrEmpty(json)) return new List<PoolField>();
            try { return JsonUtility.FromJson<PoolCache>(json).fields ?? new List<PoolField>(); }
            catch { return new List<PoolField>(); }
        }

        private static void SaveCache(List<PoolField> fields)
            => TsvrcGenerator.WriteCacheFile(CacheFile, JsonUtility.ToJson(new PoolCache { fields = fields }, true));

        private static void LogDelta(List<PoolField> previous, List<PoolField> current)
        {
            var previousIds = previous.Select(f => f.Id).ToHashSet();
            var currentIds = current.Select(f => f.Id).ToHashSet();

            foreach (var f in current.Where(f => !previousIds.Contains(f.Id)))
                Debug.Log($"[PoolModule] [WirePool] added: {f.TypeName}.{f.FieldName}");

            foreach (var f in previous.Where(f => !currentIds.Contains(f.Id)))
                Debug.Log($"[PoolModule] [WirePool] removed: {f.TypeName}.{f.FieldName}");

            if (current.Count == 0)
                Debug.Log("[PoolModule] No [WirePool] fields found.");
        }

        [Serializable]
        private class PoolField
        {
            public string TypeName;
            public string FieldName;
            public string FieldTypeName;
            public string FieldTypeNamespace;
            public string Id => $"{TypeName}.{FieldName}";
        }

        [Serializable]
        private class PoolCache
        {
            public List<PoolField> fields = new List<PoolField>();
        }
    }
}
#endif
