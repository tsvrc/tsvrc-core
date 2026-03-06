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

            w.BlankLine();
            foreach (var entry in entries)
                w.Line($"[SerializeField] private {entry.Type.Name} _{LowerFirst(entry.FieldName)};");

            foreach (var entry in entries)
            {
                w.BlankLine();
                EmitFactoryMethod(w, entry);
            }

            // Deactivate templates so they cost nothing at runtime.
            w.BlankLine();
            using (w.Block("protected void Start()"))
            {
                foreach (var entry in entries)
                    w.Line($"_{LowerFirst(entry.FieldName)}.gameObject.SetActive(false);");
            }
        }

        private static void EmitFactoryMethod(CsWriter w, TsvrcEntry entry)
        {
            string name = entry.Type.Name;
            string field = entry.FieldName;
            string lower = LowerFirst(field);

            w.Summary($"Instantiates a new <see cref=\"{name}\"/> from its inactive template.");

            if (entry.FactoryUsed)
            {
                using (w.Block($"public {name} Create{field}()"))
                {
                    w.Line("var go = Instantiate(_{lower}.gameObject);".Replace("{lower}", lower));
                    w.Line("go.SetActive(true);");
                    w.Line($"var b = go.GetComponent<{name}>();");
                    w.Line("b.TsConstruct(this);");
                    w.Line("return b;");
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
