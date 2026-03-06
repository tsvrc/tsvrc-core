#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    // Generates CompiledTsvrcConfig.cs — bootstrap that wires singletons and starts the instance.
    internal static class TsvrcInstanceBuilder
    {
        internal static string Build(TsvrcScanResult result)
        {
            var usings = CollectUsings(result);
            var startupLines = BuildStartupLines(result);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace("Tsvrc.Core.Compiled"))
            using (w.Class("public", "CompiledTsvrcConfig", "UdonSharpBehaviour"))
            {
                w.Line($"[SerializeField] private CompiledTsvrc _ts;");

                if (result.InstanceType != null)
                    w.Line($"[SerializeField] private {result.InstanceType.Name} _instance;");

                w.BlankLine();

                using (w.Block("protected void Start()"))
                {
                    foreach (var line in startupLines)
                        w.Line(line);
                }
            }

            return w.ToString();
        }

        private static SortedSet<string> CollectUsings(TsvrcScanResult result)
        {
            var usings = new SortedSet<string> { "UdonSharp", "UnityEngine", "Tsvrc.Core.Compiled" };

            // Singleton namespaces only — instance file doesn't need behaviour namespaces.
            foreach (var group in result.Groups)
                if (group.Kind == TsvrcGroupKind.Singleton)
                    foreach (var entry in group.Entries)
                        if (!string.IsNullOrEmpty(entry.Type.Namespace))
                            usings.Add(entry.Type.Namespace);

            // Instance type namespace — only when a subclass was detected.
            if (result.InstanceType != null && !string.IsNullOrEmpty(result.InstanceType.Namespace))
                usings.Add(result.InstanceType.Namespace);

            return usings;
        }

        private static List<string> BuildStartupLines(TsvrcScanResult result)
        {
            var lines = new List<string>();

            // TsConstruct all used TsvrcBehaviour singletons before the instance.
            foreach (var group in result.Groups)
                if (group.Kind == TsvrcGroupKind.Singleton)
                    foreach (var entry in group.Entries)
                        if (entry.IsTsvrcBehaviour && entry.SingletonUsed)
                            lines.Add($"_ts.{entry.FieldName}.TsConstruct(_ts);");

            // Wire and start the instance (only when a subclass was detected).
            if (result.InstanceType != null)
            {
                lines.Add("_instance.TsConstruct(_ts);");
                lines.Add("_instance.OnInstanceStart();");
            }

            return lines;
        }
    }
}
#endif
