#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Resolves Object[] entries into TsvrcEntry instances.
    // Handles: GameObject→component disambiguation, __Name__ override, Animator suffix, dedup.
    internal static class TsvrcEntryResolver
    {
        internal static bool Resolve(
            UnityEngine.Object[] source,
            TsvrcGroup group,
            HashSet<string> usedNames)
        {
            if (source == null || source.Length == 0)
                return true;

            foreach (var obj in source)
            {
                if (obj == null)
                {
                    Debug.LogWarning($"[TsvrcCompiler] Null entry in {group.Label} \u2014 skipped.");
                    continue;
                }

                Type type = obj.GetType();

                // If a whole GameObject was dropped, find the single non-Transform component.
                if (type == typeof(GameObject))
                {
                    var go = (GameObject)obj;
                    var candidates = new List<Component>();
                    foreach (var c in go.GetComponents<Component>())
                        if (!(c is Transform) && c.GetType().Name != "UdonBehaviour")
                            candidates.Add(c);

                    if (candidates.Count == 1)
                    {
                        type = candidates[0].GetType();
                    }
                    else
                    {
                        var cnames = new List<string>();
                        foreach (var c in candidates) cnames.Add(c.GetType().Name);
                        Debug.LogError(
                            $"[TsvrcCompiler] '{go.name}' ({group.Label}) has {candidates.Count} components " +
                            $"({string.Join(", ", cnames)}). Drag the specific component \u2014 not the GameObject.");
                        return false;
                    }
                }

                string goName = obj is GameObject gobj
                    ? gobj.name
                    : obj is Component comp
                        ? comp.gameObject.name
                        : null;

                string baseName = DeriveName(type, goName);
                string fieldName = Deduplicate(baseName, usedNames);
                usedNames.Add(fieldName);

                group.Entries.Add(new TsvrcEntry
                {
                    Type = type,
                    FieldName = fieldName,
                    IsTsvrcBehaviour = typeof(TsvrcBehaviour).IsAssignableFrom(type),
                    SceneObject = obj
                });
            }

            return true;
        }

        // __Name__ convention, Animator suffix, or type name fallback.
        private static string DeriveName(Type type, string goName)
        {
            if (type == typeof(Animator))
                return (CustomFieldName(goName) ?? goName) + "Animator";

            return CustomFieldName(goName) ?? type.Name;
        }

        private static string CustomFieldName(string goName)
        {
            if (goName != null
                && goName.StartsWith("__")
                && goName.EndsWith("__")
                && goName.Length > 4)
                return goName.Substring(2, goName.Length - 4);

            return null;
        }

        private static string Deduplicate(string baseName, HashSet<string> usedNames)
        {
            string name = baseName;
            int suffix = 2;
            while (usedNames.Contains(name))
                name = baseName + (suffix++);
            return name;
        }
    }
}
#endif
