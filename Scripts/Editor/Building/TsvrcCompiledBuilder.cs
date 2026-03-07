#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    // Generates CompiledTsvrc.cs — singleton fields + factory methods + bootstrap Start().
    internal static class TsvrcCompiledBuilder
    {
        internal static string Build(TsvrcScanResult result)
        {
            var usings = CollectUsings(result);
            var singletonEntries = GetEntries(result, TsvrcGroupKind.Singleton);
            var behaviourEntries = GetUsedBehaviourEntries(result);
            var constructEntries = GetEntries(result, TsvrcGroupKind.Construct);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace("Tsvrc.Core.Compiled"))
            using (w.Class("public", "CompiledTsvrc", "UdonSharpBehaviour"))
            {
                TsvrcSingletonFieldsBuilder.Emit(w, singletonEntries);
                TsvrcFactoryMethodsBuilder.Emit(w, behaviourEntries);
                EmitBootstrapFields(w, result, constructEntries);
                EmitStartMethod(w, result, behaviourEntries, constructEntries);
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
            if (result.InstanceType != null && !string.IsNullOrEmpty(result.InstanceType.Namespace))
                usings.Add(result.InstanceType.Namespace);
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
                        if (entry.FactoryUsed || entry.IsCore)
                            list.Add(entry);
            return list;
        }

        private static void EmitBootstrapFields(CsWriter w, TsvrcScanResult result, List<TsvrcEntry> constructEntries)
        {
            if (result.InstanceType == null && constructEntries.Count == 0) return;

            w.BlankLine();
            w.Region("Bootstrap");
            if (result.InstanceType != null)
                w.Line($"[SerializeField] private {result.InstanceType.Name} _instance;");
            foreach (var entry in constructEntries)
                w.Line($"[SerializeField] private {entry.Type.Name} _{LowerFirst(entry.FieldName)};");
            w.EndRegion();
        }

        private static void EmitStartMethod(CsWriter w, TsvrcScanResult result, List<TsvrcEntry> behaviourEntries, List<TsvrcEntry> constructEntries)
        {
            w.BlankLine();
            using (w.Block("protected void Start()"))
            {
                // Deactivate factory templates.
                foreach (var entry in behaviourEntries)
                    if (entry.FactoryUsed)
                        w.Line($"_{LowerFirst(entry.FieldName)}.gameObject.SetActive(false);");

                // TsConstruct singleton TsvrcBehaviours.
                foreach (var group in result.Groups)
                    if (group.Kind == TsvrcGroupKind.Singleton)
                        foreach (var entry in group.Entries)
                            if (entry.IsTsvrcBehaviour && entry.SingletonUsed)
                                w.Line($"{entry.FieldName}.TsConstruct(this);");

                // TsConstruct construct behaviours.
                foreach (var entry in constructEntries)
                    w.Line($"_{LowerFirst(entry.FieldName)}.TsConstruct(this);");

                // Wire + start instance.
                if (result.InstanceType != null)
                {
                    w.Line("_instance.TsConstruct(this);");
                    w.Line("_instance.OnInstanceStart();");
                }
            }
        }

        private static string LowerFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
#endif
