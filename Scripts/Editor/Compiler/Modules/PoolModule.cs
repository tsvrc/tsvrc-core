#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Emits one hidden private field per pool slot on CompiledTsvrc to call TsConstruct at scene start.
    // Slot count equals the number of [WirePool] declarations for each type found in user code.
    // At wire time, each slot is assigned to exactly one scene component that declares [WirePool] of that type.
    internal class PoolModule : TsvrcModule
    {
        // static readonly ensures RegexOptions.Compiled CIL is generated once per AppDomain;
        // instance Regex constructors are not cached by the regex engine.
        // Captures the field type name after [WirePool] and any extra attributes, consuming all
        // field_modifiers (§15.5.1: new, public, protected, internal, private, static, readonly,
        // volatile, unsafe) including composites like 'protected internal'. Array fields are
        // intentionally not matched; [WirePool] requires a single-reference field. The \??
        // handles nullable annotations (SomeType? → FieldInfo.FieldType.Name returns "SomeType").
        private static readonly Regex WirePoolFieldPattern = new Regex(
            @"\[WirePool(?:\([^)]*\))?\](?:\s*\[[^\]]*\])*\s*(?:(?:private|protected|public|internal|static|readonly|volatile|unsafe|new)\s+)*(\w+)\??\s+\w+\s*[=;]",
            RegexOptions.Compiled);

        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            var descriptors = ResolveDescriptors(config);

            // Slot count = number of [WirePool] declarations per type in user code.
            var declaredCounts = ScanForWirePoolAttributes();
            foreach (var field in descriptors)
                field.SlotCount = declaredCounts.TryGetValue(field.Type, out int count) ? count : 0;

            ValidatePoolDeclarations(descriptors, declaredCounts);

            _fields = descriptors.OrderBy(f => f.Name).ToList();
        }

        // Wire-only scan: count existing hidden init fields in the compiled type to restore slot counts.
        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            var descriptors = ResolveDescriptors(config);

            // Pre-compute prefixes once. Sort by length descending so that a more-specific prefix
            // (e.g. "_pool_Foo_Bar_") is always tested before a prefix that is its own prefix
            // (e.g. "_pool_Foo_"). This matters when a __custom_name__ override produces a field
            // name that starts with another descriptor's name followed by "_", since StartsWith
            // would otherwise mis-attribute slots to the shorter descriptor.
            var prefixedDescriptors = descriptors
                .Select(d => (d, PoolInitFieldPrefix(d)))
                .OrderByDescending(x => x.Item2.Length)
                .ToList();

            var slotCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fname in compiledType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(f => f.Name))
            {
                foreach (var (d, prefix) in prefixedDescriptors)
                {
                    if (fname.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        slotCounts.TryGetValue(d.Name, out int c);
                        slotCounts[d.Name] = c + 1;
                        break;
                    }
                }
            }

            var fields = new List<TsvrcField>();
            foreach (var field in descriptors)
            {
                if (!slotCounts.TryGetValue(field.Name, out int slotCount) || slotCount == 0) continue;
                field.SlotCount = slotCount;
                field.WireAlways = true;
                fields.Add(field);
            }

            _fields = fields.OrderBy(f => f.Name).ToList();
        }

        // Only prefab assets are accepted; scene objects are skipped with a warning.
        private List<TsvrcField> ResolveDescriptors(TsvrcConfig config)
        {
            var internalConfig = TsvrcCompiler.LoadInternalConfig();
            var internalPool = internalConfig?.PoolPrefabs ?? Array.Empty<TsvrcProcess>();
            var all = (config.PooledObjects ?? Array.Empty<UnityEngine.Object>())
                .Union(internalPool.Cast<UnityEngine.Object>());

            var prefabsOnly = new List<UnityEngine.Object>();
            foreach (var obj in all)
            {
                if (obj == null) continue;
                if (!EditorUtility.IsPersistent(obj))
                {
                    Debug.LogWarning($"[TsvrcPool] '{obj.name}' is a scene object. Pool entries must be prefab assets from the Project window. Skipping.");
                    continue;
                }

                // Resolve to the UdonSharpBehaviour component. If a GameObject was registered,
                // extract its first UdonSharpBehaviour component automatically.
                var component = obj as UdonSharpBehaviour
                    ?? (obj as GameObject)?.GetComponent<UdonSharpBehaviour>();
                if (component == null)
                {
                    Debug.LogWarning($"[TsvrcPool] '{obj.name}' is not an UdonSharpBehaviour. Pool entries must be UdonSharpBehaviour prefab components. Skipping.");
                    continue;
                }

                prefabsOnly.Add(component);
            }

            return TsvrcResolver.Resolve(prefabsOnly);
        }

        internal override IEnumerable<string> GetUsings() =>
            _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            var activeFields = _fields.Where(f => f.SlotCount > 0).ToList();
            if (activeFields.Count == 0) return;

            foreach (var field in activeFields)
                for (int i = 0; i < field.SlotCount; i++)
                    w.Line($"[HideInInspector] [SerializeField] private {field.Type} {PoolInitFieldName(field, i)};");
        }

        internal override void WriteStartBody(CsWriter w)
        {
            foreach (var field in _fields.Where(f => f.SlotCount > 0 && f.IsTsvrcBehaviour))
                for (int i = 0; i < field.SlotCount; i++)
                    w.Line($"{PoolInitFieldName(field, i)}.TsConstruct(this);");
        }

        internal override void Wire(SerializedObject target)
        {
            var compiled = target.targetObject as Component;
            if (compiled == null) return;
            var compiledGo = compiled.gameObject;

            // Always destroy the old Pool container first, even if there are no fields to wire.
            // If all [WirePool] declarations were removed in the last compile, _fields is empty
            // and the early-return below would otherwise leave stale pool objects in the scene.
            var existingContainer = compiledGo.transform.Find("Pool");
            if (existingContainer != null)
                Undo.DestroyObjectImmediate(existingContainer.gameObject);

            var fieldsToWire = _fields.Where(f => f.WireAlways || f.SlotCount > 0).ToList();
            if (fieldsToWire.Count == 0) return;

            GameObject poolContainer = null;
            var wireTargetsByType = CollectWireTargetsByType(compiledGo.scene);

            foreach (var field in fieldsToWire)
            {
                var source = field.SourceObject as Component;
                if (source == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Pool entry '{field.Name}' source is not a Component. Remove the invalid entry from TsvrcConfig and recompile.");
                    continue;
                }

                wireTargetsByType.TryGetValue(field.Type, out var targets);
                var sourceType = source.GetType();

                for (int i = 0; i < field.SlotCount; i++)
                {
                    if (poolContainer == null)
                    {
                        poolContainer = new GameObject("Pool");
                        Undo.RegisterCreatedObjectUndo(poolContainer, "Create Pool Container");
                        poolContainer.transform.SetParent(compiledGo.transform, false);
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject, poolContainer.transform);
                    if (instance == null)
                    {
                        Debug.LogWarning($"[TsvrcWirer] Failed to instantiate pool prefab '{field.Name}' (slot {i}).");
                        continue;
                    }

                    instance.name = $"{field.Name}_{i}";
                    Undo.RegisterCreatedObjectUndo(instance, $"Create {field.Name} pool slot {i}");

                    var instanceComponent = instance.GetComponent(sourceType);
                    if (instanceComponent == null)
                    {
                        Debug.LogWarning($"[TsvrcWirer] Pool instance '{instance.name}' is missing component '{sourceType.Name}'. Skipping slot {i}.");
                        Undo.DestroyObjectImmediate(instance);
                        continue;
                    }

                    // Wire to CompiledTsvrc hidden init field.
                    var initFieldName = PoolInitFieldName(field, i);
                    var initProp = target.FindProperty(initFieldName);
                    if (initProp != null)
                        initProp.objectReferenceValue = instanceComponent;
                    else
                        Debug.LogWarning($"[TsvrcWirer] Could not find pool init field '{initFieldName}' on CompiledTsvrc. Run Tsvrc > Tools > Force Compile to regenerate it.");

                    // Wire to the i-th scene component that declared [WirePool] of this type.
                    if (targets != null && i < targets.Count)
                    {
                        var (behaviour, fieldName) = targets[i];
                        var behaviourSo = new SerializedObject(behaviour);
                        var prop = FindSerializedProperty(behaviourSo, fieldName);

                        if (prop == null)
                            Debug.LogWarning($"[TsvrcWirer] '{behaviour.GetType().Name}.{fieldName}' has [WirePool] but is not serializable. " +
                                             $"Make the field public or add [SerializeField].");
                        else
                        {
                            prop.objectReferenceValue = instanceComponent;
                            behaviourSo.ApplyModifiedProperties();
                        }
                    }
                }

                // Warn when scene [WirePool] count doesn't match slot count.
                // Under: orphaned slots receive no scene reference. Over: stale serialized values
                // remain on scene components (ApplyModifiedProperties only flushes properties
                // explicitly assigned in this pass).
                int targetCount = targets?.Count ?? 0;
                if (targetCount < field.SlotCount)
                    Debug.LogWarning($"[TsvrcWirer] Pool type '{field.Type}': {field.SlotCount} slot(s) created but only {targetCount} scene [WirePool] target(s) found. " +
                        $"{field.SlotCount - targetCount} pool slot(s) will not be assigned to any scene component.");
                else if (targetCount > field.SlotCount)
                    Debug.LogWarning($"[TsvrcWirer] Pool type '{field.Type}': {targetCount} scene [WirePool] target(s) found but only {field.SlotCount} slot(s) exist. " +
                        $"{targetCount - field.SlotCount} scene component(s) will retain stale pool references. Re-run compile to regenerate the correct slot count.");
            }
        }

        private Dictionary<string, List<(MonoBehaviour, string)>> CollectWireTargetsByType(UnityEngine.SceneManagement.Scene scene)
        {
            var result = new Dictionary<string, List<(MonoBehaviour, string)>>(StringComparer.Ordinal);

            foreach (var rootGo in scene.GetRootGameObjects())
            {
                foreach (var behaviour in rootGo.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    foreach (var fieldInfo in GetAllInstanceFields(behaviour.GetType()))
                    {
                        if (fieldInfo.GetCustomAttribute<WirePoolAttribute>() == null) continue;

                        if (fieldInfo.FieldType.IsArray)
                        {
                            Debug.LogWarning($"[TsvrcPool] '{behaviour.GetType().Name}.{fieldInfo.Name}' is an array type. [WirePool] requires a single-reference field, not an array. Skipping.");
                            continue;
                        }

                        // IsGenericType covers List<T>, Dictionary<K,V>, etc. [WirePool] requires a
                        // single component reference; assigning objectReferenceValue to a Generic
                        // SerializedProperty is a no-op. IsGenericType is false for arrays, making
                        // this check complementary to the IsArray check above.
                        if (fieldInfo.FieldType.IsGenericType)
                        {
                            Debug.LogWarning($"[TsvrcPool] '{behaviour.GetType().Name}.{fieldInfo.Name}' is a generic type ({fieldInfo.FieldType.Name}). [WirePool] requires a single-reference field. Skipping.");
                            continue;
                        }

                        string typeName = fieldInfo.FieldType.Name;
                        if (!result.TryGetValue(typeName, out var list))
                            result[typeName] = list = new List<(MonoBehaviour, string)>();

                        list.Add((behaviour, fieldInfo.Name));
                    }
                }
            }

            return result;
        }

        // GetFields without DeclaredOnly does not return private fields from base classes,
        // so we walk the hierarchy explicitly. We stop at UdonSharpBehaviour (not just MonoBehaviour)
        // to exclude its private framework fields (serializationData, _udonSharpBackingUdonBehaviour)
        // from [WirePool] scanning.
        private static IEnumerable<FieldInfo> GetAllInstanceFields(Type type)
        {
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(UdonSharpBehaviour); t = t.BaseType)
                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    yield return f;
        }

        // FindProperty only searches the type's own declared fields, not inherited ones.
        // The iterator must start with Next(true) to descend from the root (-1) into depth-0
        // properties; subsequent Next(false) calls navigate siblings without entering struct
        // sub-fields, preventing false name matches on nested properties.
        private static SerializedProperty FindSerializedProperty(SerializedObject so, string name)
        {
            var prop = so.FindProperty(name);
            if (prop != null) return prop;
            var iter = so.GetIterator();
            bool enter = true;
            while (iter.Next(enter))
            {
                enter = false;
                if (iter.name == name) return iter.Copy();
            }
            return null;
        }

        private Dictionary<string, int> ScanForWirePoolAttributes()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var (_, src, _) in SourceScanner.GetProcessedSources())
            {
                foreach (Match m in WirePoolFieldPattern.Matches(src))
                {
                    string typeName = m.Groups[1].Value;
                    counts.TryGetValue(typeName, out int c);
                    counts[typeName] = c + 1;
                }
            }

            return counts;
        }

        private void ValidatePoolDeclarations(List<TsvrcField> descriptors, Dictionary<string, int> declaredCounts)
        {
            foreach (var group in descriptors.GroupBy(f => f.Type, StringComparer.Ordinal).Where(g => g.Count() > 1))
                Debug.LogWarning($"[TsvrcPool] Pool type '{group.Key}' appears {group.Count()} times in PooledObjects. " +
                    $"Each type can only be registered once. Remove the duplicates: {string.Join(", ", group.Select(f => f.Name))}");

            var descriptorTypes = new HashSet<string>(descriptors.Select(f => f.Type), StringComparer.Ordinal);

            foreach (var kvp in declaredCounts)
                if (!descriptorTypes.Contains(kvp.Key))
                    Debug.LogWarning($"[TsvrcPool] Pool type '{kvp.Key}' has [WirePool] attribute but is not registered in TsvrcConfig.PooledObjects.");

            foreach (var descriptor in descriptors)
                if (!declaredCounts.ContainsKey(descriptor.Type))
                    Debug.LogWarning($"[TsvrcPool] PooledObjects entry '{descriptor.Name}' ({descriptor.Type}) has no [WirePool] declaration. Remove it or add [WirePool] to a field.");
        }

        private static string PoolInitFieldPrefix(TsvrcField field) => $"_pool_{field.Name}_";
        private static string PoolInitFieldName(TsvrcField field, int index) => $"_pool_{field.Name}_{index}";
    }
}
#endif
