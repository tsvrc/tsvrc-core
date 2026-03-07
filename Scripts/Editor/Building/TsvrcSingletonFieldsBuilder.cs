#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    internal static class TsvrcSingletonFieldsBuilder
    {
        internal static void Emit(CsWriter w, IList<TsvrcEntry> entries)
        {
            bool? currentRegion = null; // null = no region open yet

            foreach (var entry in entries)
            {
                // Open or transition region.
                if (currentRegion == null || currentRegion != entry.IsCore)
                {
                    if (currentRegion != null)
                    {
                        w.EndRegion();
                        w.BlankLine();
                    }
                    w.Region(entry.IsCore ? "Core — internal framework, do not modify" : "User");
                    currentRegion = entry.IsCore;
                }

                if (entry.SingletonUsed)
                {
                    w.Summary($"Tsvrc singleton — wired by TsvrcSceneWirer.");
                    w.Line($"[SerializeField] public {entry.Type.Name} {entry.FieldName};");
                }
                else
                {
                    w.Summary($"Unused singleton — not referenced in code, hidden from inspector.");
                    w.Line($"[HideInInspector] [SerializeField] public {entry.Type.Name} {entry.FieldName} = null;");
                }
            }

            if (currentRegion != null)
                w.EndRegion();
        }
    }
}
#endif
