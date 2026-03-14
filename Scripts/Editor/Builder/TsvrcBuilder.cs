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
            w.Usings(new[] { "UdonSharp", "UnityEngine" });

            using (w.Namespace("Tsvrc.Core.Compiled"))
            using (w.Class("public", "CompiledTsvrc2", "UdonSharpBehaviour"))
            {

            }

            return w.ToString();
        }
    }
}