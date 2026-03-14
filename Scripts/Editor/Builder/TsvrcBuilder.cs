using System.Collections.Generic;
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal static class TsvrcBuilder
    {
        internal static string Build(TsvrcConfig config, List<ScanResult> scanResults)
        {
            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();

            var usings = CollectUsings(scanResults);
            w.Usings(usings);

            using (w.Namespace("Tsvrc.Core.Compiled"))
            using (w.Class("public", "CompiledTsvrc2", "UdonSharpBehaviour"))
            {
                foreach (var result in scanResults)
                    result.Builder.BuildFields(w, config, result);
            }

            return w.ToString();
        }

        private static SortedSet<string> CollectUsings(List<ScanResult> scanResults)
        {
            var usings = new SortedSet<string>();
            var defaultUsings = new[] { "UdonSharp", "UnityEngine" };
            usings.UnionWith(defaultUsings);

            foreach (var result in scanResults)
                foreach (var ns in result.Namespaces)
                    usings.Add(ns);
            return usings;
        }
    }
}