#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    internal static class TsvrcSingletonFieldsBuilder
    {
        internal static void Emit(CsWriter w, IList<TsvrcEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.SingletonUsed)
                {
                    w.Summary($"Tsvrc singleton \u2014 wired by TsvrcSceneWirer.");
                    w.Line($"[SerializeField] public {entry.Type.Name} {entry.FieldName};");
                }
                else
                {
                    w.Summary($"Unused singleton \u2014 not referenced in code, hidden from inspector.");
                    w.Line($"[HideInInspector] [SerializeField] public {entry.Type.Name} {entry.FieldName} = null;");
                }
            }
        }
    }
}
#endif
