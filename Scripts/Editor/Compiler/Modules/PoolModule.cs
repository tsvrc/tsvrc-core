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
    /// <summary>
    /// Scans config.TsvrcProcessPool and emits per-slot private fields plus Get{Type}() accessor methods.
    /// Slots are instantiated in the scene by the compiler so VRChat assigns them fixed network IDs.
    /// Slot count equals the total Get{Type}() call sites across all classes, ensuring each caller
    /// can hold its maximum number of slots simultaneously.
    /// Only <see cref="TsvrcProcess"/> subclasses are valid pool entries.
    /// </summary>
    internal class PoolModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            var descriptors = ResolveDescriptors(config);

            // Single source-tree walk for all pool types.
            var patterns = descriptors.ToDictionary(
                f => f.Name,
                f => new Regex(@"\b_ts\s*\.\s*Get" + Regex.Escape(f.Type) + @"\s*\(\s*\)"));
            var callSiteMap = SourceScanner.FindCallSitesBatch(patterns);

            foreach (var field in descriptors)
            {
                field.CallSites = callSiteMap[field.Name];
                field.SlotCount = field.CallSites.Count;
            }

            _fields = descriptors.OrderBy(f => f.Name).ToList();
        }

        // Wire-only scan: uses reflection to determine slot counts from the compiled type.
        // No source file scanning, avoids the cost of FindCallSitesBatch on every wire pass.
        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            var descriptors = ResolveDescriptors(config);

            var typeFieldNames = new HashSet<string>(compiledType
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Select(f => f.Name));

            var fields = new List<TsvrcField>();
            foreach (var field in descriptors)
            {
                string prefix = "_" + ToCamelCase(field.Name) + "_";
                int slotCount = typeFieldNames.Count(n => n.StartsWith(prefix));
                if (slotCount == 0) continue;
                field.SlotCount = slotCount;
                field.WireAlways = true;
                fields.Add(field);
            }

            _fields = fields.OrderBy(f => f.Name).ToList();
        }

        // Returns resolved field descriptors from config without scanning source files.
        // Only prefab assets are accepted, scene objects are skipped with a warning.
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

            var compiledGo = ((Component)target.targetObject).gameObject;

            // Destroy any previous pool container so repeated wire passes don't stack duplicate slots.
            var existingContainer = compiledGo.transform.Find("Pool");
            if (existingContainer != null)
                Undo.DestroyObjectImmediate(existingContainer.gameObject);

            var poolContainer = new GameObject("Pool");
            Undo.RegisterCreatedObjectUndo(poolContainer, "Create Pool Container");
            poolContainer.transform.SetParent(compiledGo.transform, false);

            foreach (var field in fieldsToWire)
            {
                var source = field.SourceObject as Component;
                if (source == null) continue;

                for (int i = 0; i < field.SlotCount; i++)
                {
                    var prop = target.FindProperty(SlotFieldName(field, i));
                    if (prop == null) continue;

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject, poolContainer.transform);
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
