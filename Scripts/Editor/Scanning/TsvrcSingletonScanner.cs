#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class TsvrcSingletonScanner
    {
        internal static TsvrcGroup Scan(TsvrcConfig config, HashSet<string> usedNames)
        {
            var group = new TsvrcGroup { Label = "Singletons", Kind = TsvrcGroupKind.Singleton };

            if (!TsvrcEntryResolver.Resolve(config.Singletons, group, usedNames))
                return null;

            foreach (var entry in group.Entries)
            {
                entry.SingletonUsed = TsvrcUsageAnalyzer.IsSingletonUsed(entry.FieldName);

                if (!entry.SingletonUsed)
                    Debug.LogWarning(
                        $"[TsvrcCompiler] '{entry.FieldName}' ({entry.Type.Name}) has no " +
                        $"'_ts.{entry.FieldName}' usage \u2014 will be hidden in inspector.");
            }

            return group;
        }
    }
}
#endif
