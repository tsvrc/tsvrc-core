#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Scans config.TsvrcBehaviourConstruct and emits the Bootstrap region.
    /// All entries receive TsConstruct(this) in Start(). Entries that are TsvrcInstance
    /// subclasses are deferred to the end and also receive OnInstanceStart().
    /// </summary>
    internal class ConstructModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            var usedNames = new HashSet<string>();
            var objects = config.TsvrcBehaviourConstruct
                .Cast<Object>()
                .ToHashSet();

            _fields = TsvrcResolver.Resolve(objects, usedNames)
                .OrderBy(f => f.Name)
                .ToList();
        }

        internal override IEnumerable<string> GetUsings() =>
            _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            if (_fields.Count == 0) return;
            w.Region("Bootstrap");
            foreach (var field in _fields)
                w.Line($"[SerializeField] private {field.Type} {PrivateFieldName(field)};");
            w.EndRegion();
        }

        internal override void WriteStartBody(CsWriter w)
        {
            // Emit TsConstruct for all entries; defer TsvrcInstance to last.
            TsvrcField instanceField = null;
            foreach (var field in _fields)
            {
                if (field.SourceObject is TsvrcInstance)
                    instanceField = field;
                else
                    w.Line($"{PrivateFieldName(field)}.TsConstruct(this);");
            }

            if (instanceField != null)
            {
                w.Line($"{PrivateFieldName(instanceField)}.TsConstruct(this);");
                w.Line($"{PrivateFieldName(instanceField)}.OnInstanceStart();");
            }
        }

        internal override void Wire(SerializedObject target)
        {
            foreach (var field in _fields)
            {
                var prop = target.FindProperty(PrivateFieldName(field));
                if (prop != null)
                    prop.objectReferenceValue = field.SourceObject;
                else
                    Debug.LogWarning($"[TsvrcWirer] Bootstrap property '{PrivateFieldName(field)}' not found on CompiledTsvrc.");
            }
        }

        private static string PrivateFieldName(TsvrcField field)
            => $"_{char.ToLower(field.Name[0])}{field.Name.Substring(1)}";
    }
}
#endif
