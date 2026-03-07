#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal static class TsvrcBehaviourScanner
    {
        // Scans a factory array tagged with isCore.
        internal static TsvrcGroup ScanFactory(TsvrcBehaviour[] source, bool isCore, HashSet<string> usedNames)
        {
            string label = isCore ? "Core Factories" : "Factories";
            var group = new TsvrcGroup { Label = label, Kind = TsvrcGroupKind.Behaviour };
            TsvrcEntryResolver.Resolve(BoxArray(source), group, usedNames);

            foreach (var entry in group.Entries)
            {
                entry.IsCore = isCore;
                entry.FactoryUsed = TsvrcUsageAnalyzer.IsFactoryUsed(entry.Type);
            }

            return group;
        }

        // Builds a Construct group from a behaviour array tagged with isCore.
        internal static TsvrcGroup ScanConstruct(TsvrcBehaviour[] source, bool isCore, HashSet<string> usedNames)
        {
            string label = isCore ? "Core Constructs" : "Constructs";
            var group = new TsvrcGroup { Label = label, Kind = TsvrcGroupKind.Construct };
            TsvrcEntryResolver.Resolve(BoxArray(source), group, usedNames);

            foreach (var entry in group.Entries)
                entry.IsCore = isCore;

            return group;
        }

        private static UnityEngine.Object[] BoxArray(TsvrcBehaviour[] arr)
        {
            if (arr == null || arr.Length == 0) return null;
            var boxed = new UnityEngine.Object[arr.Length];
            for (int i = 0; i < arr.Length; i++) boxed[i] = arr[i];
            return boxed;
        }
    }
}
#endif
