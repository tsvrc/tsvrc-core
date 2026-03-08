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

            // ── Pool fields (one per call site) ──
            w.BlankLine();
            bool? currentRegion = null;
            foreach (var entry in entries)
            {
                if (!entry.FactoryUsed) continue;
                if (currentRegion == null || currentRegion != entry.IsCore)
                {
                    if (currentRegion != null) { w.EndRegion(); w.BlankLine(); }
                    w.Region(entry.IsCore ? "Core — factory pool slots (internal)" : "User — factory pool slots");
                    currentRegion = entry.IsCore;
                }
                string lower = LowerFirst(entry.FieldName);
                for (int i = 0; i < entry.CallSites.Count; i++)
                    w.Line($"[HideInInspector] [SerializeField] private {entry.Type.Name} _{lower}_{i};");
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

            w.Summary($"Returns a pre-allocated <see cref=\"{name}\"/> pool slot. Errors if all {entry.CallSites.Count} slot(s) are active.");

            if (entry.FactoryUsed)
            {
                using (w.Block($"public {name} Create{field}()"))
                {
                    for (int i = 0; i < entry.CallSites.Count; i++)
                    {
                        using (w.Block($"if (!_{lower}_{i}.IsCreated)"))
                        {
                            w.Line($"_{lower}_{i}.gameObject.SetActive(true);");
                            w.Line($"_{lower}_{i}.TsConstruct(this);");
                            w.Line($"return _{lower}_{i};");
                        }
                    }
                    w.Line($"Debug.LogError(\"[CompiledTsvrc] {name}: all {entry.CallSites.Count} pool slot(s) are already active.\");");
                    w.Line("return null;");
                }
            }
            else
            {
                using (w.Block($"public {name} Create{field}()"))
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
