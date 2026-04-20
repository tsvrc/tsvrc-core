#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Emits per-slot fields and Get{Type}() accessors for each pool type.
    // Slots are placed in the scene at compile time so VRChat assigns them stable network IDs,
    // meaning pooled objects can send and receive VRC network events.
    // Slot count is derived from call-site analysis so each concurrent caller gets its own slot.
    internal class PoolModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            var descriptors = ResolveDescriptors(config);

            // Wrapping _ts.GetX() in a shared helper and calling it N times provisions N slots,
            // because CountCallSitesBatch multiplies each call site by its enclosing method's call count.
            var patterns = descriptors.ToDictionary(
                f => f.Name,
                f => new Regex(@"\b_ts\s*\.\s*Get" + Regex.Escape(f.Type) + @"\s*\(\s*\)", RegexOptions.Compiled));
            var slotCounts = SourceScanner.CountCallSitesBatch(patterns);

            foreach (var field in descriptors)
                field.SlotCount = slotCounts[field.Name];

            _fields = descriptors.OrderBy(f => f.Name).ToList();
        }

        // Wire-only scan: uses reflection to determine slot counts from the compiled type.
        // No source file scanning, avoids the cost of FindCallSitesBatch on every wire pass.
        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            var descriptors = ResolveDescriptors(config);

            // Build prefix -> descriptor map so we can count slots in a single O(N) pass
            // over the compiled type's fields instead of O(N×M) repeated enumerations.
            var prefixToDescriptor = new Dictionary<string, TsvrcField>(StringComparer.Ordinal);
            foreach (var d in descriptors)
                prefixToDescriptor["_" + ToCamelCase(d.Name) + "_"] = d;

            var slotCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fname in compiledType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                             .Select(f => f.Name))
            {
                foreach (var kvp in prefixToDescriptor)
                    if (fname.StartsWith(kvp.Key, StringComparison.Ordinal))
                    {
                        slotCounts.TryGetValue(kvp.Value.Name, out int c);
                        slotCounts[kvp.Value.Name] = c + 1;
                        break; // each field name matches at most one pool prefix
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
            var all = (config.TsvrcProcessPool ?? Array.Empty<TsvrcProcess>())
                .Union(internalPool)
                .Cast<UnityEngine.Object>();

            var prefabsOnly = new List<UnityEngine.Object>();
            foreach (var obj in all)
            {
                if (obj == null) continue;
                if (!EditorUtility.IsPersistent(obj))
                {
                    Debug.LogWarning($"[TsvrcPool] '{obj.name}' is a scene object. Pool entries must be prefab assets from the Project window. Skipping.");
                    continue;
                }
                prefabsOnly.Add(obj);
            }

            return TsvrcResolver.Resolve(prefabsOnly);
        }

        internal override IEnumerable<string> GetUsings() =>
            _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            var activeFields = _fields.Where(f => f.SlotCount > 0).ToList();
            if (activeFields.Count == 0) return;

            w.Region("Process Pool Slots");
            foreach (var field in activeFields)
                for (int i = 0; i < field.SlotCount; i++)
                    w.Line($"[HideInInspector] [SerializeField] private {field.Type} {SlotFieldName(field, i)};");
            w.EndRegion();
        }

        internal override void WriteMethods(CsWriter w)
        {
            if (_fields.Count == 0) return;
            w.Region("Process Pool Accessors");
            foreach (var field in _fields)
            {
                if (field.SlotCount == 0)
                {
                    w.Summary(TsvrcCodeGen.StubSummary($"Get{field.Type}"));
                    using (w.Method($"public {field.Type} Get{field.Type}()"))
                    {
                        w.Line($"Debug.LogError(\"{TsvrcCodeGen.NullFieldMessage($"Get{field.Type}")}\");");
                        w.Line("return null;");
                    }
                }
                else
                {
                    w.Summary($"Gets a <see cref=\"{field.Type}\"/> at runtime. " +
                              $"Pooled objects have stable VRChat network IDs and can send and receive network events. " +
                              $"Call <see cref=\"Tsvrc.Core.TsvrcProcess.TsRelease\"/> when done to return it for reuse. " +
                              $"Logs an error if all {field.SlotCount} slot(s) are already in use.");
                    using (w.Method($"public {field.Type} Get{field.Type}()"))
                    {
                        for (int i = 0; i < field.SlotCount; i++)
                        {
                            string slot = SlotFieldName(field, i);
                            using (w.Block($"if ({slot} != null && !{slot}.IsConstructed)"))
                            {
                                w.Line($"{slot}.gameObject.SetActive(true);");
                                w.Line($"{slot}.TsConstruct(this);");
                                w.Line($"return {slot};");
                            }
                        }
                        w.Line($"Debug.LogError(\"[CompiledTsvrc] {field.Type}: all {field.SlotCount} pool slot(s) are already active.\");");
                        w.Line("return null;");
                    }
                }
            }
            w.EndRegion();
        }

        internal override void WriteStartBody(CsWriter w)
        {
            foreach (var field in _fields.Where(f => f.SlotCount > 0))
                for (int i = 0; i < field.SlotCount; i++)
                    w.Line($"{SlotFieldName(field, i)}.gameObject.SetActive(false);");
        }

        internal override void Wire(SerializedObject target)
        {
            var fieldsToWire = _fields.Where(f => f.WireAlways || f.SlotCount > 0).ToList();
            if (fieldsToWire.Count == 0) return;

            var compiled = target.targetObject as Component;
            if (compiled == null) return;
            var compiledGo = compiled.gameObject;

            // Destroy any previous pool container so repeated wire passes don't stack duplicate slots.
            var existingContainer = compiledGo.transform.Find("Pool");
            if (existingContainer != null)
                Undo.DestroyObjectImmediate(existingContainer.gameObject);

            // Create the container lazily so a scene with all-invalid entries doesn't leave an empty GameObject.
            GameObject poolContainer = null;

            foreach (var field in fieldsToWire)
            {
                var source = field.SourceObject as Component;
                if (source == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Pool entry '{field.Name}' source is not a Component. Remove the invalid entry from TsvrcConfig and recompile.");
                    continue;
                }

                if (poolContainer == null)
                {
                    poolContainer = new GameObject("Pool");
                    Undo.RegisterCreatedObjectUndo(poolContainer, "Create Pool Container");
                    poolContainer.transform.SetParent(compiledGo.transform, false);
                }

                for (int i = 0; i < field.SlotCount; i++)
                {
                    var prop = target.FindProperty(SlotFieldName(field, i));
                    if (prop == null) continue;

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject, poolContainer.transform);
                    if (instance == null)
                    {
                        Debug.LogWarning($"[TsvrcWirer] Failed to instantiate pool prefab '{field.Name}'. The prefab asset may be missing or corrupted.");
                        continue;
                    }

                    instance.name = $"{field.Name}_{i}";
                    Undo.RegisterCreatedObjectUndo(instance, $"Create {field.Name} pool slot {i}");
                    prop.objectReferenceValue = instance.GetComponent(source.GetType());
                }
            }
        }

        private static string SlotFieldName(TsvrcField field, int index)
            => $"_{ToCamelCase(field.Name)}_{index}";

        private static string ToCamelCase(string name)
            => string.IsNullOrEmpty(name) ? name : char.ToLower(name[0]) + name.Substring(1);
    }
}
#endif
