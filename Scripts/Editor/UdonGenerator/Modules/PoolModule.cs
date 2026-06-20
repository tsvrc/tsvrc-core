#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Editor.V2
{
    // Generates per-type pool slots on TsvrcGenerated and instantiates the configured
    // prefabs under a "Pool" child object at wire time. Slot count is driven by the
    // number of [WirePool] fields found across all loaded assemblies, not by the prefab
    // count — each [WirePool] field gets its own dedicated instance.
    internal class PoolModule : TsvrcModule
    {
        private bool _hasAnyConfigured;
        private List<PoolField> _currentFields = new List<PoolField>();
        private HashSet<string> _configuredTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private List<(Component prefab, string typeName)> _poolEntries = new List<(Component, string)>();
        private Dictionary<string, (string Namespace, int Count)> _activeSlots = new Dictionary<string, (string, int)>(StringComparer.Ordinal);

        internal override string FileName => "TsvrcGeneratedPool.cs";

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override void LoadConfig()
        {
            _currentFields = DetectWirePoolFields();
            // TsvrcConfig is a scene component (PooledObjects/Singletons/Constructs need to be
            // able to hold scene-object references), not an asset - found, not loaded.
            var userConfig = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
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

            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                foreach (var kvp in slotsByType.OrderBy(x => x.Key))
                    for (int i = 0; i < kvp.Value.Count; i++)
                        w.Line($"[HideInInspector] [SerializeField] private {kvp.Key} {SlotFieldName(kvp.Key, i)};");

                using (w.Method("public void _TsPoolStart()"))
                {
                    foreach (var kvp in slotsByType.OrderBy(x => x.Key))
                    {
                        if (!IsTsvrcBehaviourType(kvp.Key, kvp.Value.Namespace)) continue;
                        for (int i = 0; i < kvp.Value.Count; i++)
                            w.Line($"{SlotFieldName(kvp.Key, i)}.TsConstruct(this);");
                    }
                }
            }

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            using (w.Method("public void _TsPoolStart()"))
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
            var root = FindRoot();
            if (root == null) return false;
            return root.transform.Find("Pool") == null;
        }

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            // Config is non-empty but all entries were invalid (null, scene objects, etc.).
            // Do not destroy an existing Pool container in this state — it may be valid from
            // a previous run and the config error is likely transient.
            if (_hasAnyConfigured && _poolEntries.Count == 0) return;

            var existingContainer = root.transform.Find("Pool");
            if (existingContainer != null)
                Undo.DestroyObjectImmediate(existingContainer.gameObject);

            if (_poolEntries.Count == 0) return;

            var activeSlots = _activeSlots;
            var wireTargetsByType = CollectWireTargetsByType(root.gameObject.scene);
            var so = new SerializedObject(root);
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
                        poolContainer.transform.SetParent(root.transform, false);
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
                        Debug.LogWarning($"[PoolModule] Field '{SlotFieldName(typeName, i)}' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");

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

            ApplyAndMarkDirty(so, root);
        }

        private static string SlotFieldName(string typeName, int index) => $"_pool_{typeName}_{index}";

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

        // Builtins are processed before user config so builtin types always occupy the
        // lower slot indices — slot 0 for a given type is always the same prefab regardless
        // of how many user entries are added.
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
