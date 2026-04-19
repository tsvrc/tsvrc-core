#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;
using Tsvrc.Utils;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Built-in module that always emits a <see cref="TsMemory"/> singleton on CompiledTsvrc.
    /// Requires no user configuration — the GameObject is created automatically if missing.
    /// </summary>
    internal class MemoryModule : TsvrcModule
    {
        internal override void Scan(TsvrcConfig config) { }

        internal override IEnumerable<string> GetUsings()
        {
            yield return "Tsvrc.Utils";
        }

        internal override void WriteFields(CsWriter w)
        {
            w.Region("Memory");
            w.Summary("Built-in local key-value store. Access via <c>_ts.Memory</c>.");
            w.Line("[ReadOnly] [SerializeField] public TsMemory Memory;");
            w.EndRegion();
        }

        internal override void WriteStartBody(CsWriter w)
        {
            w.Line("Memory.TsConstruct(this);");
        }

        internal override void Wire(SerializedObject target)
        {
            var compiledTsvrc = (Component)target.targetObject;
            var memory = RequireMemory(compiledTsvrc.transform);

            var prop = target.FindProperty("Memory");
            if (prop != null)
                prop.objectReferenceValue = memory;
            else
                Debug.LogWarning("[TsvrcWirer] 'Memory' property not found on CompiledTsvrc.");
        }

        private static TsMemory RequireMemory(Transform parent)
        {
            // Check for an existing TsMemory already under CompiledTsvrc (fast child lookup, no global scene search).
            var existing = parent.GetComponentInChildren<TsMemory>(true);
            if (existing != null)
                return existing;

            var go = new GameObject("TsMemory");
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Create TsMemory");
            return UdonSharpUndo.AddComponent<TsMemory>(go);
        }
    }
}
#endif
