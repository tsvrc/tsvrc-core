#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Editor.V2
{
    internal class PoolModule : TsvrcModule
    {
        private const string BuiltinConfigPath = "Assets/Tsvrc/TsvrcBuiltinConfig.asset";

        private bool _hasAnyConfigured;
        private List<PoolField> _currentFields = new List<PoolField>();
        private HashSet<string> _configuredTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private List<(Component prefab, string typeName)> _poolEntries = new List<(Component, string)>();
        private Dictionary<string, (string Namespace, int Count)> _activeSlots = new Dictionary<string, (string, int)>(StringComparer.Ordinal);

        internal override string FileName => "TsvrcPoolBehaviour.cs";

        internal override IEnumerable<string> WatchedAssets() => new[] { ScaffoldModule.ConfigPath, BuiltinConfigPath };

        internal override void LoadConfig()
        {
            _currentFields = DetectWirePoolFields();
            var userConfig = AssetDatabase.LoadAssetAtPath<TsvrcConfig>(ScaffoldModule.ConfigPath);
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsvrcBuiltinConfig>(BuiltinConfigPath);
            _hasAnyConfigured = (userConfig?.PooledObjects?.Length > 0)
                             || (builtinConfig?.PoolPrefabs?.Length > 0);
            (_configuredTypeNames, _poolEntries) = ResolveConfig(userConfig, builtinConfig);
            _activeSlots = BuildActiveSlots();
        }

        internal override string GenerateCode()
        {
            var slotsByType = _activeSlots;
            if (slotsByType.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UdonSharp", "UnityEngine" };
            foreach (var (ns, _) in slotsByType.Values)
                if (!string.IsNullOrEmpty(ns) && !usings.Contains(ns))
                    usings.Add(ns);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.PoolClassName} : UdonSharpBehaviour"))
            {
                foreach (var kvp in slotsByType.OrderBy(x => x.Key))
                    for (int i = 0; i < kvp.Value.Count; i++)
                        w.Line($"[SerializeField] private {kvp.Key} {SlotFieldName(kvp.Key, i)};");

                using (w.Method("void Start()"))
                {
                    foreach (var kvp in slotsByType.OrderBy(x => x.Key))
                    {
                        if (!IsTsvrcBehaviourType(kvp.Key, kvp.Value.Namespace)) continue;
                        for (int i = 0; i < kvp.Value.Count; i++)
                            w.Line($"{SlotFieldName(kvp.Key, i)}.gameObject.SetActive(false);");
                    }
                }
            }

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.PoolClassName} : UdonSharpBehaviour"))
            using (w.Method("void Start()"))
            { }
            return w.ToString();
        }

        private Dictionary<string, (string Namespace, int Count)> BuildActiveSlots()
        {
            var slotsByType = new Dictionary<string, (string Namespace, int Count)>(StringComparer.Ordinal);
            foreach (var f in _currentFields)
            {
                if (!_configuredTypeNames.Contains(f.FieldTypeName)) continue;
                slotsByType.TryGetValue(f.FieldTypeName, out var entry);
                slotsByType[f.FieldTypeName] = (f.FieldTypeNamespace, entry.Count + 1);
            }
            return slotsByType;
        }

        internal override bool OnSceneHierarchyChanged()
        {
            if (_configuredTypeNames.Count == 0) return false;
            var poolType = ScaffoldModule.FindPoolType();
            if (poolType == null) return false;
            var poolComp = (Component)UnityEngine.Object.FindObjectOfType(poolType, true);
            if (poolComp == null) return false;
            return poolComp.transform.Find("Pool") == null;
        }

        internal override void Wire()
        {
            var poolType = ScaffoldModule.FindPoolType();
            if (poolType == null) return;

            var poolComp = (Component)UnityEngine.Object.FindObjectOfType(poolType, true);
            if (poolComp == null) return;

            if (_hasAnyConfigured && _poolEntries.Count == 0) return;

            var existingContainer = poolComp.transform.Find("Pool");
            if (existingContainer != null)
                Undo.DestroyObjectImmediate(existingContainer.gameObject);

            if (_poolEntries.Count == 0) return;

            var activeSlots = _activeSlots;
            var wireTargetsByType = CollectWireTargetsByType(poolComp.gameObject.scene);
            var so = new SerializedObject(poolComp);
            GameObject poolContainer = null;

            foreach (var (prefabComponent, typeName) in _poolEntries)
            {
                if (!activeSlots.TryGetValue(typeName, out var slotEntry) || slotEntry.Count == 0)
                    continue;
                int slotCount = slotEntry.Count;

                wireTargetsByType.TryGetValue(typeName, out var targets);
                var sourceType = prefabComponent.GetType();

                for (int i = 0; i < slotCount; i++)
                {
                    if (poolContainer == null)
                    {
                        poolContainer = new GameObject("Pool");
                        Undo.RegisterCreatedObjectUndo(poolContainer, "Create Pool Container");
                        poolContainer.transform.SetParent(poolComp.transform, false);
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

                    var initProp = so.FindProperty(SlotFieldName(typeName, i));
                    if (initProp != null)
                        initProp.objectReferenceValue = instanceComponent;
                    else
                        Debug.LogWarning($"[PoolModule] Field '{SlotFieldName(typeName, i)}' not found on {ScaffoldModule.PoolClassName}. Force compile to regenerate.");

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

            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(poolComp.gameObject.scene);
        }

        private static string SlotFieldName(string typeName, int index) => $"_pool_{typeName}_{index}";

        private static bool IsTsvrcBehaviourType(string shortName, string ns)
        {
            string fullName = string.IsNullOrEmpty(ns) ? shortName : $"{ns}.{shortName}";
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type == null) continue;
                for (var t = type.BaseType; t != null; t = t.BaseType)
                    if (t.Name == "TsvrcBehaviour") return true;
                return false;
            }
            return false;
        }

        // Deduplicated by (declaring type full name, field name): Unity's AppDomain can carry stale
        // duplicate copies of the same assembly across successive recompiles (domain reload doesn't
        // always fully unload the previous version before the next one loads), which would otherwise
        // make every [WirePool] field count once per duplicate and inflate slot counts on every edit.
        private static List<PoolField> DetectWirePoolFields()
        {
            var found = new List<PoolField>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var type in types)
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!IsWirePoolField(field)) continue;
                        if (!seen.Add($"{type.FullName}.{field.Name}")) continue;
                        found.Add(new PoolField
                        {
                            FieldTypeName = field.FieldType.Name,
                            FieldTypeNamespace = field.FieldType.Namespace ?? string.Empty,
                        });
                    }
            }
            return found;
        }

        private static bool IsWirePoolField(FieldInfo field)
        {
            var attrs = field.GetCustomAttributes(false);
            if (!attrs.Any(a => a.GetType().Name == "WirePoolAttribute")) return false;
            return field.IsPublic || attrs.Any(a => a.GetType().Name == "SerializeField");
        }

        // Single pass over both config sources: produces the type-name set (for slot generation)
        // and the ordered prefab entry list (for wiring) simultaneously.
        // Builtins come first so they always occupy lower slot indices.
        private static (HashSet<string> typeNames, List<(Component prefab, string typeName)> entries)
            ResolveConfig(TsvrcConfig userConfig, TsvrcBuiltinConfig builtinConfig)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<(Component, string)>();

            if (builtinConfig?.PoolPrefabs != null)
                foreach (var proc in builtinConfig.PoolPrefabs)
                {
                    if (proc == null) continue;
                    if (!EditorUtility.IsPersistent(proc))
                    {
                        Debug.LogWarning($"[PoolModule] Builtin '{proc.name}' is a scene object. Pool entries must be prefab assets. Skipping.");
                        continue;
                    }
                    var typeName = proc.GetType().Name;
                    names.Add(typeName);
                    entries.Add((proc, typeName));
                }

            if (userConfig?.PooledObjects != null)
                foreach (var obj in userConfig.PooledObjects)
                {
                    if (obj == null) continue;
                    if (!EditorUtility.IsPersistent(obj))
                    {
                        Debug.LogWarning($"[PoolModule] '{obj.name}' is a scene object. Pool entries must be prefab assets. Skipping.");
                        continue;
                    }
                    var typeName = obj.GetType().Name;
                    names.Add(typeName);
                    entries.Add((obj, typeName));
                }

            return (names, entries);
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

        private struct PoolField
        {
            public string FieldTypeName;
            public string FieldTypeNamespace;
        }
    }
}
#endif
