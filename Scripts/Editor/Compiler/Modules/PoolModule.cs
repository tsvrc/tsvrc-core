#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Scans config.TsvrcProcessPool and emits per-slot private fields plus Get{Type}() accessor methods.
    /// Slots are instantiated in the scene by the compiler so VRChat assigns them fixed network IDs.
    /// Slot count is the total number of Get{Type}() call sites across all classes, so each caller can hold its slots simultaneously.
    /// Only <see cref="TsvrcProcess"/> subclasses are valid pool entries.
    /// </summary>
    internal class PoolModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            var resolved = ResolveFields(config);

            foreach (var field in resolved)
                field.SlotCount = field.CallSites
                    .GroupBy(cs => cs.ClassName)
                    .Sum(g => g.Count());

            _fields = resolved.OrderBy(f => f.Name).ToList();
        }

        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            var resolved = ResolveFields(config);

            var typeFieldNames = new HashSet<string>(compiledType
                .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Select(f => f.Name));

            foreach (var field in resolved)
            {
                string prefix = "_" + ToCamelCase(field.Name) + "_";
                field.SlotCount = typeFieldNames.Count(f => f.StartsWith(prefix));
            }

            _fields = resolved.Where(f => f.SlotCount > 0).OrderBy(f => f.Name).ToList();
        }

        private List<TsvrcField> ResolveFields(TsvrcConfig config)
        {
            var internalConfig = TsvrcCompiler.LoadInternalConfig();
            var internalPool = internalConfig?.PoolPrefabs ?? Array.Empty<TsvrcProcess>();
            var resolved = TsvrcResolver.Resolve(
                (config.TsvrcProcessPool ?? Array.Empty<TsvrcProcess>())
                    .Union(internalPool)
                    .Cast<UnityEngine.Object>());

            foreach (var field in resolved)
            {
                var pattern = new Regex(@"\b_ts\s*\.\s*Get" + Regex.Escape(field.Type) + @"\s*\(\s*\)");
                field.CallSites = SourceScanner.FindCallSites(pattern);
            }

            return resolved;
        }

        internal override IEnumerable<string> GetUsings() =>
            _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            if (_fields.Count == 0) return;
            w.Region("Process Pool Slots");
            foreach (var field in _fields)
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
                w.Summary($"Gets a <see cref=\"{field.Type}\"/> at runtime. " +
                          $"Pooled objects have stable VRChat network IDs and can send and receive network events. " +
                          $"Call <see cref=\"Tsvrc.Core.TsvrcProcess.TsRelease\"/> when done to return it for reuse. " +
                          $"Logs an error if all {field.SlotCount} slot(s) are already in use.");
                using (w.Method($"public {field.Type} Get{field.Type}()"))
                {
                    if (field.SlotCount == 0)
                    {
                        w.Line($"Debug.LogError(\"{TsvrcCodeGen.NullFieldMessage($"Get{field.Type}")}\");");
                        w.Line("return null;");
                        continue;
                    }

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
            w.EndRegion();
        }

        internal override void WriteStartBody(CsWriter w)
        {
            foreach (var field in _fields)
                for (int i = 0; i < field.SlotCount; i++)
                    w.Line($"{SlotFieldName(field, i)}.gameObject.SetActive(false);");
        }

        internal override void Wire(SerializedObject target)
        {
            if (_fields.Count == 0) return;

            var compiledGo = ((Component)target.targetObject).gameObject;

            foreach (var field in _fields)
            {
                if (field.CallSites.Count == 0) continue;

                var source = field.SourceObject as Component;
                if (source == null) continue;

                bool isPrefab = PrefabUtility.IsPartOfPrefabAsset(source.gameObject);

                for (int i = 0; i < field.SlotCount; i++)
                {
                    var prop = target.FindProperty(SlotFieldName(field, i));
                    if (prop == null) continue;

                    var instance = isPrefab
                        ? (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject, compiledGo.transform)
                        : UnityEngine.Object.Instantiate(source.gameObject, compiledGo.transform);

                    instance.name = $"{field.Name}_{i}";
                    Undo.RegisterCreatedObjectUndo(instance, $"Create {field.Name} pool slot {i}");
                    prop.objectReferenceValue = instance.GetComponent(source.GetType());
                }
            }
        }

        internal static string SlotFieldName(TsvrcField field, int index)
            => $"_{ToCamelCase(field.Name)}_{index}";

        internal static string ToCamelCase(string name)
            => string.IsNullOrEmpty(name) ? name : char.ToLower(name[0]) + name.Substring(1);
    }
}
#endif
