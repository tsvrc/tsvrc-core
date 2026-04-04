#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Assembles the full CompiledTsvrc.cs source string from a list of modules.
    /// Each module independently contributes usings, fields, methods, and Start() lines.
    /// </summary>
    internal static class TsvrcCodeGen
    {
        private const string CompiledClassName = "CompiledTsvrc";
        private const string CompiledNamespace = "Tsvrc.Core.Compiled";
        private const string UdonSyncAttribute = "[UdonBehaviourSyncMode(BehaviourSyncMode.None)]";

        internal static string Build(IEnumerable<TsvrcModule> modules)
        {
            var moduleList = modules.ToList();
            var w = new CsWriter();

            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(CollectUsings(moduleList));

            using (w.Namespace(CompiledNamespace))
            {
                foreach (var module in moduleList)
                {
                    module.WriteBeforeClass(w);
                }

                using (w.Class("public", CompiledClassName, "UdonSharpBehaviour", UdonSyncAttribute))
                {
                    foreach (var module in moduleList)
                        module.WriteFields(w);

                    foreach (var module in moduleList)
                        module.WriteMethods(w);

                    WriteStart(w, moduleList);
                }
            }

            return w.ToString();
        }

        private static void WriteStart(CsWriter w, List<TsvrcModule> modules)
        {
            using (w.Method("protected void Start()"))
            {
                foreach (var module in modules)
                    module.WriteStartBody(w);
            }
        }

        private static SortedSet<string> CollectUsings(List<TsvrcModule> modules)
        {
            var usings = new SortedSet<string> { "Tsvrc.Utils", "UdonSharp", "UnityEngine" };
            foreach (var module in modules)
                foreach (var ns in module.GetUsings())
                    if (!string.IsNullOrEmpty(ns))
                        usings.Add(ns);
            return usings;
        }

        // Emits the standard runtime error for a field that exists in CompiledTsvrc but has no scene reference.
        internal static string NullFieldMessage(string fieldName)
            => $"[Tsvrc] '{fieldName}' is null. It was not in use when Tsvrc was last compiled. Recompile Tsvrc to activate it.";
    }
}
#endif
