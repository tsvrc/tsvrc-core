#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;

namespace Tsvrc.Editor
{
    // Assembles the full CompiledTsvrc.cs source string from a list of modules.
    // Each module independently contributes usings, fields, methods, and Start() body lines.
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
            var usings = new SortedSet<string> { "UdonSharp", "UnityEngine" };
            foreach (var module in modules)
                foreach (var ns in module.GetUsings())
                    if (!string.IsNullOrEmpty(ns))
                        usings.Add(ns);
            return usings;
        }

        // Runtime error body for generated stubs. Used inside Debug.LogError() in CompiledTsvrc.
        internal static string NullFieldMessage(string memberName)
            => $"[Tsvrc] '{memberName}' was not in use when Tsvrc last compiled. Recompile Tsvrc to activate it.";

        // XML doc summary for generated stub members. IDE-friendly; no log-prefix clutter.
        internal static string StubSummary(string memberName)
            => $"Inactive, <c>{memberName}</c> had no call sites at last compile. Recompile Tsvrc to enable it.";
    }
}
#endif
