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
    /// Scans config.Singletons and emits typed public fields on CompiledTsvrc.
    /// Only entries referenced as _ts.FieldName somewhere in user code are included.
    /// </summary>
    internal class SingletonModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            var internalSingletons = config.InternalTsvrcConfig?.Singletons ?? Array.Empty<UnityEngine.Object>();
            var objects = config.Singletons.Union(internalSingletons).ToHashSet();
            var resolved = TsvrcResolver.Resolve(objects, new HashSet<string>());

            foreach (var field in resolved)
                field.CallSites = SourceScanner.FindCallSites(
                    new Regex(@"\b_ts\s*\.\s*" + Regex.Escape(field.Name) + @"\b"));

            _fields = resolved.OrderBy(f => f.Name).ToList();
        }

        internal override IEnumerable<string> GetUsings() =>
            _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            if (_fields.Count == 0) return;
            w.Region("Singletons");
            foreach (var field in _fields)
            {
                w.Summary("Tsvrc singleton — wired by TsvrcWirer.");
                w.Line($"[SerializeField] public {field.Type} {field.Name};");
            }
            w.EndRegion();
        }

        internal override void WriteStartBody(CsWriter w)
        {
            foreach (var field in _fields)
                if (field.SourceObject is TsvrcBehaviour && field.CallSites.Count > 0)
                    w.Line($"{field.Name}.TsConstruct(this);");
        }

        internal override void Wire(SerializedObject target)
        {
            foreach (var field in _fields)
            {
                var prop = target.FindProperty(field.Name);
                if (prop == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Singleton property '{field.Name}' not found on CompiledTsvrc.");
                    continue;
                }
                prop.objectReferenceValue = field.CallSites.Count > 0 ? field.SourceObject : null;
            }
        }
    }
}
#endif
