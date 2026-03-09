#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    internal static class TsvrcGetMethodsBuilder
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
                if (!entry.GetterUsed) continue;
                if (currentRegion == null || currentRegion != entry.IsCore)
                {
                    if (currentRegion != null) { w.EndRegion(); w.BlankLine(); }
                    w.Region(entry.IsCore ? "Core — pool slots (internal)" : "User — pool slots");
                    currentRegion = entry.IsCore;
                }
                string lower = LowerFirst(entry.FieldName);
                for (int i = 0; i < entry.CallSites.Count; i++)
                    w.Line($"[HideInInspector] [SerializeField] private {entry.Type.Name} _{lower}_{i};");
            }
            if (currentRegion != null) w.EndRegion();

            // ── Get methods ──
            currentRegion = null;
            foreach (var entry in entries)
            {
                if (currentRegion == null || currentRegion != entry.IsCore)
                {
                    if (currentRegion != null) { w.EndRegion(); }
                    w.BlankLine();
                    w.Region(entry.IsCore ? "Core — get methods (internal)" : "User — get methods");
                    currentRegion = entry.IsCore;
                }
                w.BlankLine();
                EmitGetMethod(w, entry);
            }
            if (currentRegion != null) w.EndRegion();
        }

        private static void EmitGetMethod(CsWriter w, TsvrcEntry entry)
        {
            string name = entry.Type.Name;
            string field = entry.FieldName;
            string lower = LowerFirst(field);

            w.Summary($"Activates and returns a scene-placed <see cref=\"{name}\"/> pool slot (preserves VRChat network ID). Errors if all {entry.CallSites.Count} slot(s) are active.");

            if (entry.GetterUsed)
            {
                using (w.Block($"public {name} Get{field}()"))
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
                using (w.Block($"public {name} Get{field}()"))
                {
                    w.Line($"Debug.LogError(\"[CompiledTsvrc] {name} has no Get{name}() call site.\");");
                    w.Line("return null;");
                }
            }
        }

        private static string LowerFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
#endif
