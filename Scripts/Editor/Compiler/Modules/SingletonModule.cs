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
    // Emits typed public fields for each configured singleton.
    // Singletons with no call sites in user code are emitted as error-logging stubs instead of real fields.
    internal class SingletonModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            var resolved = ResolveDescriptors(config);

            var patterns = resolved.ToDictionary(
                f => f.Name,
                f => new Regex(@"\b_ts\s*\.\s*" + Regex.Escape(f.Name) + @"\b", RegexOptions.Compiled));
            var callSiteMap = SourceScanner.FindCallSitesBatch(patterns);

            foreach (var field in resolved)
                field.CallSiteCount = callSiteMap[field.Name];

            _fields = resolved.OrderBy(f => f.Name).ToList();
        }

        // Wire-only scan: uses reflection to find which singleton fields exist in the compiled type.
        // No source file scanning, avoids FindCallSitesBatch on every wire pass.
        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            var resolved = ResolveDescriptors(config);

            var fields = new List<TsvrcField>();
            foreach (var field in resolved)
            {
                // Active singletons are emitted as serialized fields; stubs are computed properties.
                // A public instance field with the exact name confirms this is an active entry.
                if (compiledType.GetField(field.Name,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) == null)
                    continue;
                field.WireAlways = true;
                fields.Add(field);
            }

            _fields = fields.OrderBy(f => f.Name).ToList();
        }

        // Shared between Scan and ScanForWire. Does not touch source files.
        private List<TsvrcField> ResolveDescriptors(TsvrcConfig config)
        {
            var internalConfig = TsvrcCompiler.LoadInternalConfig();
            var internalSingletons = internalConfig?.Singletons ?? Array.Empty<UnityEngine.Object>();
            return TsvrcResolver.Resolve(
                (config.Singletons ?? Array.Empty<UnityEngine.Object>()).Union(internalSingletons));
        }

        internal override IEnumerable<string> GetUsings() =>
            _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            if (_fields.Count == 0) return;
            w.Region("Singletons");
            foreach (var field in _fields)
            {
                if (field.CallSiteCount == 0)
                {
                    w.Summary(TsvrcCodeGen.StubSummary(field.Name));
                    w.Line($"public {field.Type} {field.Name} {{ get {{ Debug.LogError(\"{TsvrcCodeGen.NullFieldMessage(field.Name)}\"); return null; }} }}");
                }
                else
                {
                    w.Summary("Tsvrc singleton.");
                    w.Line($"[HideInInspector] [SerializeField] public {field.Type} {field.Name};");
                }
            }
            w.EndRegion();
        }

        internal override void WriteStartBody(CsWriter w)
        {
            foreach (var field in _fields)
                if (field.CallSiteCount > 0 && field.SourceObject is TsvrcBehaviour)
                    w.Line($"{field.Name}.TsConstruct(this);");
        }

        internal override void Wire(SerializedObject target)
        {
            // Stubs are computed properties, not serialized fields, skip them to avoid spurious warnings.
            var fieldsToWire = _fields.Where(f => f.WireAlways || f.CallSiteCount > 0).ToList();
            foreach (var field in fieldsToWire)
            {
                var prop = target.FindProperty(field.Name);
                if (prop == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Singleton property '{field.Name}' not found on CompiledTsvrc.");
                    continue;
                }
                prop.objectReferenceValue = field.SourceObject;
            }
        }
    }
}
#endif
