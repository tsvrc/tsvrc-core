#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal static class TsvrcBehaviourScanner
    {
        // Merges TsvrcBehaviourFactory + InternalTsvrcBehaviourFactory into one factory group.
        internal static TsvrcGroup ScanFactory(TsvrcConfig config, HashSet<string> usedNames)
        {
            var group = new TsvrcGroup { Label = "Factories", Kind = TsvrcGroupKind.Behaviour };
            var source = MergeArrays(config.TsvrcBehaviourFactory, config.InternalTsvrcBehaviourFactory);
            TsvrcEntryResolver.Resolve(source, group, usedNames);

            foreach (var entry in group.Entries)
                entry.FactoryUsed = TsvrcUsageAnalyzer.IsFactoryUsed(entry.Type);

            return group;
        }

        // Builds a Construct group from TsvrcBehaviourConstruct — wired by scene, TsConstruct called at start.
        internal static TsvrcGroup ScanConstruct(TsvrcConfig config, HashSet<string> usedNames)
        {
            var group = new TsvrcGroup { Label = "Constructs", Kind = TsvrcGroupKind.Construct };
            var source = BoxArray(config.TsvrcBehaviourConstruct);
            TsvrcEntryResolver.Resolve(source, group, usedNames);
            return group;
        }

        private static UnityEngine.Object[] MergeArrays(TsvrcBehaviour[] a, TsvrcBehaviour[] b)
        {
            int aLen = a?.Length ?? 0;
            int bLen = b?.Length ?? 0;
            var merged = new UnityEngine.Object[aLen + bLen];
            for (int i = 0; i < aLen; i++) merged[i] = a[i];
            for (int i = 0; i < bLen; i++) merged[aLen + i] = b[i];
            return merged;
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
