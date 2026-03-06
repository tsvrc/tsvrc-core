#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    // Generates CompiledTsvrc.cs — singleton fields + behaviour factory methods.
    internal static class TsvrcCompiledBuilder
    {
        internal static string Build(TsvrcScanResult result)
        {
            var usings = CollectUsings(result);
            var singletonEntries = GetEntries(result, TsvrcGroupKind.Singleton);
            var behaviourEntries = GetUsedBehaviourEntries(result);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace("Tsvrc.Core.Compiled"))
            using (w.Class("public", "CompiledTsvrc", "UdonSharpBehaviour"))
            {
                TsvrcSingletonFieldsBuilder.Emit(w, singletonEntries);
                TsvrcFactoryMethodsBuilder.Emit(w, behaviourEntries);
            }

            return w.ToString();
        }

        private static SortedSet<string> CollectUsings(TsvrcScanResult result)
        {
            var usings = new SortedSet<string> { "UdonSharp", "UnityEngine" };
            foreach (var group in result.Groups)
                foreach (var entry in group.Entries)
                    if (!string.IsNullOrEmpty(entry.Type.Namespace))
                        usings.Add(entry.Type.Namespace);
            return usings;
        }

        private static List<TsvrcEntry> GetEntries(TsvrcScanResult result, TsvrcGroupKind kind)
        {
            var list = new List<TsvrcEntry>();
            foreach (var group in result.Groups)
                if (group.Kind == kind)
                    list.AddRange(group.Entries);
            return list;
        }

        private static List<TsvrcEntry> GetUsedBehaviourEntries(TsvrcScanResult result)
        {
            var list = new List<TsvrcEntry>();
            foreach (var group in result.Groups)
                if (group.Kind == TsvrcGroupKind.Behaviour)
                    foreach (var entry in group.Entries)
                        if (entry.FactoryUsed)
                            list.Add(entry);
            return list;
        }
    }
}
#endif
