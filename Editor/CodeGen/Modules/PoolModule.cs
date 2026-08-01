#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UdonSharp;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Editor
{
    // Generates per-type pool slots on TsGenerated and instantiates prefabs under a "Pool"
    // child at wire time. Slot counts come from a dependency graph: external scene refs plus
    // contributions from parent pool types, so nested [WirePool] fields inside pool types
    // produce the correct slot totals.
    internal class PoolModule : TsModule
    {
        private const string SnapshotKey = "PoolModule";

        private List<(Component prefab, string typeName)> _poolEntries = new List<(Component, string)>();
        private Dictionary<string, PoolTypeInfo> _poolTypeInfos = new Dictionary<string, PoolTypeInfo>(StringComparer.Ordinal);
        private HashSet<string> _watchedTypeNames = new HashSet<string>(StringComparer.Ordinal);

        // Set when LoadConfig() had to fall back to the snapshot this pass. Wire() checks this
        // to avoid its own destructive behavior, tearing down the real "Pool" container when
        // _poolEntries looks empty, based on a currently unreliable, compile broken live scan.
        // See Wire()'s own guard for why this matters more here than for Singleton, Factory, and
        // Construct, none of which destroy existing scene state on empty input.
        private bool _usedSnapshotFallback;

        private class PoolTypeInfo
        {
            public Component Prefab;
            public string TypeName;
            public string TypeNamespace;
            public int ExternalCount;
            public Dictionary<string, int> InternalDeps = new Dictionary<string, int>(StringComparer.Ordinal);
            public int TotalSlots;
        }

        internal override string FileName => "TsGeneratedPool.cs";

        internal override string TabLabel => "Pool";
        internal override string TabDescription =>
            "Register UdonSharpBehaviour prefabs to pool. " +
            "The system automatically instantiates all slots, initializes them, and wires every [WirePool] field across your behaviours at compile time. " +
            "No manual scene placement, no cross-behaviour drag-and-drop, and no broken references when you refactor.";
        internal override void DrawTab(SerializedObject so) => ObjectListGUI.DrawObjectList(so, "PooledObjects",
            "No pooled prefabs registered yet. Add a prefab here to make it available for network-synced spawning.",
            assetsOnly: true);

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override IEnumerable<string> WatchedComponentTypeNames() => _watchedTypeNames;

        internal override void LoadConfig()
        {
            var userConfig = TsLinkedScene.Find<TsConfig>();
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>(BuiltinConfigPath);

            _poolEntries = ResolveConfig(userConfig, builtinConfig);

            // Same type may appear in both builtinConfig and userConfig, so keep the first
            // occurrence. Builtins come first in ResolveConfig, so builtin slots always have
            // lower indices.
            var seenEntryTypes = new HashSet<string>(StringComparer.Ordinal);
            _poolEntries = _poolEntries.Where(e => seenEntryTypes.Add(e.typeName)).ToList();

            _poolTypeInfos = new Dictionary<string, PoolTypeInfo>(StringComparer.Ordinal);
            _watchedTypeNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (prefab, typeName) in _poolEntries)
            {
                _poolTypeInfos[typeName] = new PoolTypeInfo
                {
                    Prefab = prefab,
                    TypeName = typeName,
                    TypeNamespace = prefab.GetType().Namespace ?? string.Empty,
                };
            }

            ScanExternalRefs();
            ScanInternalDeps();
            ComputeTotalSlots();

            // Protects the type list and per-type slot counts GenerateCode() emits, not Wire()'s
            // scene wiring. A type that only survives via the snapshot has no live Prefab
            // reference here, since fromSnapshot leaves it null, so Wire() can't instantiate it.
            // See TsModule.ApplySnapshotFallback's doc comment for the same accepted contract
            // Singleton, Factory, and Construct already have. TotalSlots is itself derived from
            // a live, scene-wide [WirePool] reflection scan, ScanExternalRefs and
            // ScanInternalDeps, just as fragile to a broken compile as the entry list, so it is
            // snapshotted here too via Entry.SlotCount rather than recomputed for restored
            // entries.
            var liveInfos = _poolTypeInfos.Values.ToList();
            var effectiveInfos = ApplySnapshotFallback(SnapshotKey, liveInfos,
                i => new ModuleEntrySnapshot.Entry { Name = i.TypeName, TypeName = i.TypeName, Namespace = i.TypeNamespace, SlotCount = i.TotalSlots },
                e => new PoolTypeInfo { Prefab = null, TypeName = e.TypeName, TypeNamespace = e.Namespace, TotalSlots = e.SlotCount });

            _usedSnapshotFallback = !ReferenceEquals(effectiveInfos, liveInfos);
            if (_usedSnapshotFallback)
            {
                _poolTypeInfos = effectiveInfos.ToDictionary(i => i.TypeName, StringComparer.Ordinal);
                _poolEntries = _poolEntries.Where(e => _poolTypeInfos.ContainsKey(e.typeName)).ToList();
            }
        }

        // Count [WirePool] fields on non-pool scene behaviour instances; each field-per-instance
        // contributes one external slot for the referenced type.
        private void ScanExternalRefs()
        {
            var scene = TsLinkedScene.SceneToScan;
            if (scene == null) return;
            foreach (var rootGo in scene.Value.GetRootGameObjects())
                foreach (var behaviour in rootGo.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    // GetComponentsInChildren<MonoBehaviour> includes a null entry for any
                    // "missing script" component whose type can't be resolved. Skip those
                    // rather than crashing the whole generator pass on one broken reference.
                    if (behaviour == null) continue;
                    if (_poolTypeInfos.ContainsKey(behaviour.GetType().Name)) continue;
                    for (var t = behaviour.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(UdonSharpBehaviour); t = t.BaseType)
                        foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            if (!IsWirePoolField(field)) continue;
                            if (field.FieldType.IsArray || field.FieldType.IsGenericType) continue;
                            if (!_poolTypeInfos.TryGetValue(field.FieldType.Name, out var info)) continue;
                            info.ExternalCount++;
                            _watchedTypeNames.Add(t.Name);
                        }
                }
        }

        // Reflect each configured pool type's class for its own [WirePool] fields pointing at
        // other pool types; these become InternalDeps entries driving the slot count formula.
        private void ScanInternalDeps()
        {
            foreach (var info in _poolTypeInfos.Values)
            {
                for (var t = info.Prefab.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(UdonSharpBehaviour); t = t.BaseType)
                    foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!IsWirePoolField(field)) continue;
                        if (field.FieldType.IsArray || field.FieldType.IsGenericType) continue;
                        var depName = field.FieldType.Name;
                        if (!_poolTypeInfos.ContainsKey(depName)) continue;
                        info.InternalDeps.TryGetValue(depName, out var count);
                        info.InternalDeps[depName] = count + 1;
                        _watchedTypeNames.Add(t.Name);
                    }
            }
        }

        // Topological DFS: totalSlots(T) = externalCount(T) + Σ_P wiresFromP(T) × totalSlots(P)
        // Must resolve parent slots before child slots (parent = pool type that references T).
        private void ComputeTotalSlots()
        {
            // reverseMap[dep] = list of (parentTypeName, fieldCount in parent referencing dep)
            var reverseMap = new Dictionary<string, List<(string, int)>>(StringComparer.Ordinal);
            foreach (var info in _poolTypeInfos.Values)
                foreach (var (depName, count) in info.InternalDeps)
                {
                    if (!reverseMap.TryGetValue(depName, out var list))
                        reverseMap[depName] = list = new List<(string, int)>();
                    list.Add((info.TypeName, count));
                }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var inStack = new HashSet<string>(StringComparer.Ordinal);
            foreach (var typeName in _poolTypeInfos.Keys.ToList())
                ComputeForType(typeName, visited, inStack, reverseMap);
        }

        private int ComputeForType(string typeName, HashSet<string> visited, HashSet<string> inStack,
            Dictionary<string, List<(string, int)>> reverseMap)
        {
            if (visited.Contains(typeName))
                return _poolTypeInfos[typeName].TotalSlots;
            if (inStack.Contains(typeName))
            {
                Debug.LogError($"[PoolModule] Circular dependency detected involving '{typeName}'. Excluding from pool generation.");
                return 0;
            }
            if (!_poolTypeInfos.TryGetValue(typeName, out var info))
                return 0;

            inStack.Add(typeName);
            int total = info.ExternalCount;
            if (reverseMap.TryGetValue(typeName, out var parents))
                foreach (var (parentName, fieldCount) in parents)
                    total += fieldCount * ComputeForType(parentName, visited, inStack, reverseMap);

            inStack.Remove(typeName);
            visited.Add(typeName);
            info.TotalSlots = total;
            return total;
        }

        internal override string GenerateCode()
        {
            var eligible = _poolTypeInfos.Values.Where(i => i.TotalSlots > 0).OrderBy(i => i.TypeName).ToList();
            if (eligible.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UdonSharp", "UnityEngine" };
            foreach (var info in eligible)
                if (!string.IsNullOrEmpty(info.TypeNamespace) && !usings.Contains(info.TypeNamespace))
                    usings.Add(info.TypeNamespace);

            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                foreach (var info in eligible)
                    for (int i = 0; i < info.TotalSlots; i++)
                        w.Line($"[HideInInspector] [SerializeField] private {info.TypeName} {SlotFieldName(info.TypeName, i)};");

                using (w.Method("public void _TsPoolStart()"))
                {
                    foreach (var info in eligible)
                    {
                        if (!IsTsvrcBehaviourType(info.TypeName, info.TypeNamespace)) continue;
                        for (int i = 0; i < info.TotalSlots; i++)
                            w.Line($"{SlotFieldName(info.TypeName, i)}.TsConstruct(this);");
                    }
                }
            }

            return w.ToString();
        }

        private static string BuildStub() => BuildStub(new[] { "UdonSharp", "UnityEngine" }, "public void _TsPoolStart()");

        internal override bool OnSceneHierarchyChanged()
        {
            var root = FindRoot();
            if (root == null) return false;
            var pool = root.transform.Find("Pool");
            if (_poolEntries.Count == 0) return pool != null;
            if (pool == null) return true;
            int expected = _poolTypeInfos.Values.Sum(v => v.TotalSlots);
            return pool.childCount != expected;
        }

        internal override void Wire()
        {
            // A fallback pass means the live scan this run is unreliable, typically because
            // compilation is currently broken. Leave whatever is already wired in the scene
            // completely alone rather than risk destroying a real, working "Pool" container
            // based on _poolEntries looking emptier than it really is right now.
            if (_usedSnapshotFallback) return;

            var root = FindRoot();
            if (root == null) return;

            var existingContainer = root.transform.Find("Pool");

            if (_poolEntries.Count == 0)
            {
                if (existingContainer != null)
                    Undo.DestroyObjectImmediate(existingContainer.gameObject);
                return;
            }

            // Pool instances from a prior run are already in the scene, so
            // CollectWireTargetsByType discovers their internal [WirePool] fields too, letting
            // IsPoolAlreadyWired validate them.
            var wireTargetsByType = CollectWireTargetsByType(root.gameObject.scene);

            if (IsPoolAlreadyWired(root, existingContainer, wireTargetsByType)) return;

            if (existingContainer != null)
                Undo.DestroyObjectImmediate(existingContainer.gameObject);

            var so = new SerializedObject(root);
            GameObject poolContainer = null;
            var allInstances = new Dictionary<string, List<Component>>(StringComparer.Ordinal);

            // Phase 1: instantiate all pool slots and assign _pool_* fields on TsGenerated.
            foreach (var (prefabComponent, typeName) in _poolEntries)
            {
                if (!_poolTypeInfos.TryGetValue(typeName, out var info) || info.TotalSlots == 0)
                    continue;

                var sourceType = prefabComponent.GetType();
                var instances = new List<Component>();
                allInstances[typeName] = instances;

                for (int i = 0; i < info.TotalSlots; i++)
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
                        instances.Add(null);
                        continue;
                    }

                    instance.name = $"{typeName}_{i}";
                    Undo.RegisterCreatedObjectUndo(instance, $"Create {typeName} pool slot {i}");

                    var instanceComponent = instance.GetComponent(sourceType);
                    if (instanceComponent == null)
                    {
                        Debug.LogWarning($"[PoolModule] Instance '{instance.name}' is missing component '{sourceType.Name}'. Skipping slot {i}.");
                        Undo.DestroyObjectImmediate(instance);
                        instances.Add(null);
                        continue;
                    }

                    instances.Add(instanceComponent);

                    if (TryFindField(so, SlotFieldName(typeName, i), "PoolModule", out var initProp))
                        initProp.objectReferenceValue = instanceComponent;
                }
            }

            // Phase 2: collect wire targets again now that pool instances exist in the scene,
            // then assign all [WirePool] fields (including internal ones on pool instances).
            var updatedWireTargets = CollectWireTargetsByType(root.gameObject.scene);

            foreach (var (_, typeName) in _poolEntries)
            {
                if (!allInstances.TryGetValue(typeName, out var instances)) continue;

                var info = _poolTypeInfos[typeName];
                updatedWireTargets.TryGetValue(typeName, out var targets);
                int targetCount = targets?.Count ?? 0;

                for (int i = 0; i < info.TotalSlots; i++)
                {
                    if (i >= instances.Count || instances[i] == null) continue;
                    if (targets == null || i >= targets.Count) continue;

                    var (behaviour, fieldName) = targets[i];
                    var behaviourSo = new SerializedObject(behaviour);
                    var prop = behaviourSo.FindProperty(fieldName);
                    if (prop != null)
                    {
                        prop.objectReferenceValue = instances[i];
                        behaviourSo.ApplyModifiedProperties();
                    }
                    else
                        Debug.LogWarning($"[PoolModule] '{behaviour.GetType().Name}.{fieldName}' has [WirePool] but is not serialized. Make it public or add [SerializeField].");
                }

                if (targetCount < info.TotalSlots)
                    Debug.LogWarning($"[PoolModule] '{typeName}': {info.TotalSlots} slot(s), {targetCount} [WirePool] target(s) — {info.TotalSlots - targetCount} slot(s) unassigned.");
                else if (targetCount > info.TotalSlots)
                    Debug.LogWarning($"[PoolModule] '{typeName}': {targetCount} [WirePool] target(s), {info.TotalSlots} slot(s) — {targetCount - info.TotalSlots} component(s) will keep stale references.");
            }

            ApplyAndMarkDirty(so, root);
        }

        private bool IsPoolAlreadyWired(Component root, Transform existingContainer, Dictionary<string, List<(MonoBehaviour, string)>> wireTargets)
        {
            if (existingContainer == null) return false;

            int expectedTotal = _poolTypeInfos.Values.Sum(v => v.TotalSlots);
            if (existingContainer.childCount != expectedTotal) return false;

            SerializedObject so = null;

            foreach (var (prefabComponent, typeName) in _poolEntries)
            {
                if (!_poolTypeInfos.TryGetValue(typeName, out var info) || info.TotalSlots == 0)
                    continue;

                var prefabGo = prefabComponent.gameObject;
                var prefabType = prefabComponent.GetType();
                wireTargets.TryGetValue(typeName, out var targets);

                for (int i = 0; i < info.TotalSlots; i++)
                {
                    var childTransform = existingContainer.Find($"{typeName}_{i}");
                    if (childTransform == null) return false;

                    if (PrefabUtility.GetCorrespondingObjectFromSource(childTransform.gameObject) != prefabGo)
                        return false;

                    var childComp = childTransform.GetComponent(prefabType);
                    if (childComp == null) return false;

                    if (so == null) so = new SerializedObject(root);
                    var prop = so.FindProperty(SlotFieldName(typeName, i));
                    if (prop == null || prop.objectReferenceValue != childComp) return false;

                    if (targets != null && i < targets.Count)
                    {
                        var (behaviour, fieldName) = targets[i];
                        var targetSo = new SerializedObject(behaviour);
                        var targetProp = targetSo.FindProperty(fieldName);
                        if (targetProp == null || targetProp.objectReferenceValue != childComp) return false;
                    }
                }
            }

            return true;
        }

        private static string SlotFieldName(string typeName, int index) => $"_pool_{typeName}_{index}";

        private static bool IsWirePoolField(FieldInfo field)
        {
            var attrs = field.GetCustomAttributes(false);
            if (!attrs.Any(a => a.GetType().Name == "WirePoolAttribute")) return false;
            return field.IsPublic || attrs.Any(a => a.GetType().Name == "SerializeField");
        }

        // Builtins are processed before user config so builtin types always occupy the lower
        // slot indices. For example, slot 0 stays the same prefab regardless of added user
        // entries.
        private static List<(Component prefab, string typeName)> ResolveConfig(TsConfig userConfig, TsBuiltinConfig builtinConfig)
        {
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
                    entries.Add((proc, proc.GetType().Name));
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
                    entries.Add((obj, obj.GetType().Name));
                }

            return entries;
        }

        private static Dictionary<string, List<(MonoBehaviour, string)>> CollectWireTargetsByType(Scene scene)
        {
            var result = new Dictionary<string, List<(MonoBehaviour, string)>>(StringComparer.Ordinal);

            foreach (var rootGo in scene.GetRootGameObjects())
                foreach (var behaviour in rootGo.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    for (var t = behaviour.GetType(); t != null && t != typeof(MonoBehaviour) && t != typeof(UdonSharpBehaviour); t = t.BaseType)
                        foreach (var field in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            // IsWirePoolField excludes non-serialized private [WirePool] fields,
                            // matching ScanExternalRefs and ScanInternalDeps. Otherwise the
                            // target and slot mismatch warning below would misreport by one.
                            if (!IsWirePoolField(field)) continue;
                            if (field.FieldType.IsArray || field.FieldType.IsGenericType) continue;

                            string typeName = field.FieldType.Name;
                            if (!result.TryGetValue(typeName, out var list))
                                result[typeName] = list = new List<(MonoBehaviour, string)>();
                            list.Add((behaviour, field.Name));
                        }
                }

            return result;
        }
    }
}
#endif
