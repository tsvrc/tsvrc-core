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

            var slotCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fname in compiledType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance).Select(f => f.Name))
            {
                foreach (var d in descriptors)
                {
                    if (fname.StartsWith(PoolInitFieldPrefix(d), StringComparison.Ordinal))
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
            var internalPool = internalConfig != null ? internalConfig.PoolPrefabs : Array.Empty<TsvrcProcess>();
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

            w.Region("Pool Init References");
            foreach (var field in activeFields)
                for (int i = 0; i < field.SlotCount; i++)
                    w.Line($"[HideInInspector] [SerializeField] private {field.Type} {PoolInitFieldName(field, i)};");
            w.EndRegion();
        }

        internal override void WriteStartBody(CsWriter w)
        {
            foreach (var field in _fields.Where(f => f.SlotCount > 0 && f.IsTsvrcBehaviour))
                for (int i = 0; i < field.SlotCount; i++)
                    w.Line($"{PoolInitFieldName(field, i)}.TsConstruct(this);");
        }

        internal override void Wire(SerializedObject target)
        {
            var fieldsToWire = _fields.Where(f => f.WireAlways || f.SlotCount > 0).ToList();
            if (fieldsToWire.Count == 0) return;

            var compiled = target.targetObject as Component;
            if (compiled == null) return;
            var compiledGo = compiled.gameObject;

            var existingContainer = compiledGo.transform.Find("Pool");
            if (existingContainer != null)
                Undo.DestroyObjectImmediate(existingContainer.gameObject);

            GameObject poolContainer = null;

            // Collect all scene MonoBehaviours with [WirePool] fields, grouped by type name.
            // Each component is assigned one slot in declaration order.
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

                    var instanceComponent = instance.GetComponent(source.GetType());

                    // Wire to CompiledTsvrc hidden init field.
                    var initProp = target.FindProperty(PoolInitFieldName(field, i));
                    if (initProp != null)
                        initProp.objectReferenceValue = instanceComponent;

                    // Wire to the i-th scene component that declared [WirePool] of this type.
                    if (targets != null && i < targets.Count)
                    {
                        var (behaviour, fieldName) = targets[i];
                        var behaviourSo = new SerializedObject(behaviour);
                        var prop = behaviourSo.FindProperty(fieldName);

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
            }
        }

        /// <summary>
        /// Scans all scene MonoBehaviours for [WirePool] fields, grouped by pool type name.
        /// Each entry is (component, fieldName) in scene hierarchy order.
        /// </summary>
        private Dictionary<string, List<(MonoBehaviour, string)>> CollectWireTargetsByType(UnityEngine.SceneManagement.Scene scene)
        {
            var result = new Dictionary<string, List<(MonoBehaviour, string)>>(StringComparer.Ordinal);

            foreach (var rootGo in scene.GetRootGameObjects())
            {
                foreach (var behaviour in rootGo.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    foreach (var fieldInfo in behaviour.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (fieldInfo.GetCustomAttribute<WirePoolAttribute>() == null) continue;

                        string typeName = fieldInfo.FieldType.Name;
                        if (!result.ContainsKey(typeName))
                            result[typeName] = new List<(MonoBehaviour, string)>();

                        result[typeName].Add((behaviour, fieldInfo.Name));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Scans source code for [WirePool] field declarations.
        /// Returns a dictionary of type name → number of declarations found.
        /// </summary>
        private Dictionary<string, int> ScanForWirePoolAttributes()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            // Handles both attribute orderings:
            //   [WirePool] [SerializeField] private Type field;
            //   [SerializeField] [WirePool] private Type field;
            var pattern = new Regex(
                @"(?:\[SerializeField\]\s*)?\[WirePool(?:\([^)]*\))?\]\s*(?:\[SerializeField\]\s*)?(?:private|protected|public|internal)?\s*(\w+)\s+\w+\s*[=;]",
                RegexOptions.Compiled);

            foreach (var (_, src, _) in SourceScanner.GetProcessedSources())
            {
                foreach (Match m in pattern.Matches(src))
                {
                    string typeName = m.Groups[1].Value;
                    counts.TryGetValue(typeName, out int c);
                    counts[typeName] = c + 1;
                }
            }

            return counts;
        }

        /// <summary>
        /// Validates [WirePool] declarations against registered pools and detects duplicate type registrations.
        /// </summary>
        private void ValidatePoolDeclarations(List<TsvrcField> descriptors, Dictionary<string, int> declaredCounts)
        {
            var settings = TsvrcCompilerSettings.Instance;

            foreach (var group in descriptors.GroupBy(f => f.Type, StringComparer.Ordinal).Where(g => g.Count() > 1))
            {
                ReportValidation(settings.PoolValidation,
                    $"Pool type '{group.Key}' appears {group.Count()} times in PooledObjects. " +
                    $"Each type can only be registered once. Remove the duplicates: {string.Join(", ", group.Select(f => f.Name))}");
            }

            foreach (var kvp in declaredCounts)
            {
                if (!descriptors.Any(f => f.Type == kvp.Key))
                    ReportValidation(settings.PoolValidation,
                        $"Pool type '{kvp.Key}' has [WirePool] attribute but is not registered in TsvrcConfig.PooledObjects.");
            }

            foreach (var descriptor in descriptors)
            {
                if (!declaredCounts.ContainsKey(descriptor.Type))
                    ReportValidation(settings.PoolValidation,
                        $"PooledObjects entry '{descriptor.Name}' ({descriptor.Type}) has no [WirePool] declaration. Remove it or add [WirePool] to a field.");
            }
        }

        private void ReportValidation(ValidationLevel level, string message)
        {
            switch (level)
            {
                case ValidationLevel.Error:   throw new InvalidOperationException($"[TsvrcPool] {message}");
                case ValidationLevel.Warn:    Debug.LogWarning($"[TsvrcPool] {message}"); break;
                case ValidationLevel.Silent:  break;
            }
        }

        // Hidden field prefix on CompiledTsvrc used only for TsConstruct calls.
        private static string PoolInitFieldPrefix(TsvrcField field) => $"_pool_{field.Name}_";
        private static string PoolInitFieldName(TsvrcField field, int index) => $"_pool_{field.Name}_{index}";
    }
}
#endif
