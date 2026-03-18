#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Handles config.Instance — the single TsvrcInstance for the world.
    /// Emits a private field in the Bootstrap region and calls TsConstruct + OnInstanceStart in Start().
    /// </summary>
    internal class InstanceModule : TsvrcModule
    {
        private TsvrcField _field;

        internal override void Scan(TsvrcConfig config)
        {
            if (config.Instance == null)
            {
                _field = null;
                return;
            }

            var resolved = TsvrcResolver.Resolve(
                new HashSet<Object> { config.Instance },
                new HashSet<string>());

            _field = resolved.FirstOrDefault();
        }

        internal override IEnumerable<string> GetUsings()
        {
            if (_field != null && !string.IsNullOrEmpty(_field.Namespace))
                yield return _field.Namespace;
        }

        internal override void WriteFields(CsWriter w)
        {
            if (_field == null) return;
            w.Line($"[SerializeField] private {_field.Type} _instance;");
        }

        internal override void WriteStartBody(CsWriter w)
        {
            if (_field == null) return;
            w.Line("_instance.TsConstruct(this);");
            w.Line("_instance.OnInstanceStart();");
        }

        internal override void Wire(SerializedObject target)
        {
            if (_field == null) return;
            var prop = target.FindProperty("_instance");
            if (prop != null)
                prop.objectReferenceValue = _field.SourceObject;
            else
                Debug.LogWarning("[TsvrcWirer] '_instance' property not found on CompiledTsvrc.");
        }
    }
}
#endif
