#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class TsvrcSingletonScanner
    {
        internal static TsvrcGroup Scan(UnityEngine.Object[] singletons, bool isCore, HashSet<string> usedNames)
        {
            string label = isCore ? "Core Singletons" : "Singletons";
            var group = new TsvrcGroup { Label = label, Kind = TsvrcGroupKind.Singleton };

            if (!TsvrcEntryResolver.Resolve(singletons, group, usedNames))
                return null;

            foreach (var entry in group.Entries)
            {
                entry.IsCore = isCore;
                entry.SingletonUsed = TsvrcUsageAnalyzer.IsSingletonUsed(entry.FieldName);

                if (!entry.SingletonUsed && !isCore)
                    Debug.LogWarning(
                        $"[TsvrcCompiler] '{entry.FieldName}' ({entry.Type.Name}) has no " +
                        $"'_ts.{entry.FieldName}' usage \u2014 will be hidden in inspector.");
            }

            return group;
        }
    }
}
#endif
