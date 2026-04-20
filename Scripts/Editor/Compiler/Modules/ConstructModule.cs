#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Unlike singletons and factories, constructs are always wired. No call-site scanning is needed.
    internal class ConstructModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            _fields = ResolveFields(config);
        }

        // Only wire constructs whose fields exist in the compiled type. New entries added
        // after the last full compile are skipped until the user recompiles.
        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            var activeFieldNames = new HashSet<string>(
                compiledType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => f.Name));

            _fields = ResolveFields(config)
                .Where(f => activeFieldNames.Contains(FieldName(f)))
                .ToList();
        }

        internal override IEnumerable<string> GetUsings() =>
            _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            if (_fields.Count == 0) return;
            w.Region("Constructs");
            foreach (var field in _fields)
                w.Line($"[HideInInspector] [SerializeField] private {field.Type} {FieldName(field)};");
            w.EndRegion();
        }

        internal override void WriteStartBody(CsWriter w)
        {
            foreach (var field in _fields)
                w.Line($"{FieldName(field)}.TsConstruct(this);");
        }

        internal override void Wire(SerializedObject target)
        {
            foreach (var field in _fields)
            {
                if (field.SourceObject == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Construct '{field.Name}' source object is null, remove the missing entry from TsvrcConfig and recompile.");
                    continue;
                }

                var prop = target.FindProperty(FieldName(field));
                if (prop != null)
                    prop.objectReferenceValue = field.SourceObject;
                else
                    Debug.LogWarning($"[TsvrcWirer] Construct property '{FieldName(field)}' not found on CompiledTsvrc.");
            }
        }

        private static List<TsvrcField> ResolveFields(TsvrcConfig config)
        {
            return TsvrcResolver.Resolve(config.TsvrcBehaviourConstruct)
                .OrderBy(f => f.Name)
                .ToList();
        }

        private static string FieldName(TsvrcField field) => $"_construct{field.Name}";
    }
}
#endif
