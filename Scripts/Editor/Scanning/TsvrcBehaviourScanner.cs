#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal static class TsvrcBehaviourScanner
    {
        internal static TsvrcGroup Scan(TsvrcConfig config, HashSet<string> usedNames)
        {
            var group = new TsvrcGroup { Label = "Behaviours", Kind = TsvrcGroupKind.Behaviour };

            // Box TsvrcBehaviour[] to Object[] for the shared resolver.
            UnityEngine.Object[] source = null;
            if (config.Behaviours != null)
            {
                source = new UnityEngine.Object[config.Behaviours.Length];
                for (int i = 0; i < config.Behaviours.Length; i++)
                    source[i] = config.Behaviours[i];
            }

            if (!TsvrcEntryResolver.Resolve(source, group, usedNames))
                return null;

            foreach (var entry in group.Entries)
                entry.FactoryUsed = TsvrcUsageAnalyzer.IsFactoryUsed(entry.Type);

            return group;
        }
    }
}
#endif
