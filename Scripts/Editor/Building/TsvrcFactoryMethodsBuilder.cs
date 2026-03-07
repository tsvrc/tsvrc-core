#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    internal static class TsvrcFactoryMethodsBuilder
    {
        internal static void Emit(CsWriter w, IList<TsvrcEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            // ── Template fields ──
            w.BlankLine();
            bool? currentRegion = null;
            foreach (var entry in entries)
            {
                if (currentRegion == null || currentRegion != entry.IsCore)
                {
                    if (currentRegion != null) { w.EndRegion(); w.BlankLine(); }
                    w.Region(entry.IsCore ? "Core — factory templates (internal)" : "User — factory templates");
                    currentRegion = entry.IsCore;
                }
                w.Line($"[HideInInspector] [SerializeField] private {entry.Type.Name} _{LowerFirst(entry.FieldName)};");
            }
            if (currentRegion != null) w.EndRegion();

            // ── Factory methods ──
            currentRegion = null;
            foreach (var entry in entries)
            {
                if (currentRegion == null || currentRegion != entry.IsCore)
                {
                    if (currentRegion != null) { w.EndRegion(); }
                    w.BlankLine();
                    w.Region(entry.IsCore ? "Core — factory methods (internal)" : "User — factory methods");
                    currentRegion = entry.IsCore;
                }
                w.BlankLine();
                EmitFactoryMethod(w, entry);
            }
            if (currentRegion != null) w.EndRegion();
        }

        private static void EmitFactoryMethod(CsWriter w, TsvrcEntry entry)
        {
            string name = entry.Type.Name;
            string field = entry.FieldName;
            string lower = LowerFirst(field);

            w.Summary($"Instantiates a new <see cref=\"{name}\"/> from its inactive template.");

            if (entry.FactoryUsed)
            {
                using (w.Block($"public {name} Create{field}(Transform parent)"))
                {
                    w.Line("var go = Instantiate(_{lower}.gameObject, parent);".Replace("{lower}", lower));
                    w.Line("go.SetActive(true);");
                    w.Line($"var b = go.GetComponent<{name}>();");
                    w.Line("b.TsConstruct(this);");
                    w.Line("return b;");
                }
            }
            else
            {
                using (w.Block($"public {name} Create{field}(Transform parent)"))
                {
                    w.Line($"Debug.LogError(\"[CompiledTsvrc] {name} has no Create{name}() call site.\");");
                    w.Line("return null;");
                }
            }
        }

        private static string LowerFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
#endif
