#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class TsvrcResolver
    {
        // Resolves a collection of Unity objects into TsvrcField descriptors.
        // Null entries and duplicate object references are both skipped with a warning.
        // Pass an existing usedNames set to share deduplication across multiple calls.
        internal static List<TsvrcField> Resolve(
            IEnumerable<UnityEngine.Object> objects,
            HashSet<string> usedNames = null)
        {
            var fields = new List<TsvrcField>();
            if (objects == null) return fields;

            usedNames ??= new HashSet<string>();
            var seen = new HashSet<UnityEngine.Object>();

            foreach (var obj in objects)
            {
                if (obj == null)
                {
                    Debug.LogWarning("[TsvrcResolver] Null entry in config, remove the missing-script slot from TsvrcConfig and recompile.");
                    continue;
                }

                if (!seen.Add(obj))
                {
                    Debug.LogWarning($"[TsvrcResolver] Duplicate entry '{obj.name}' in config, remove the duplicate from TsvrcConfig.");
                    continue;
                }

                var type = obj.GetType();
                string goName = obj switch
                {
                    Component c => c.gameObject.name,
                    GameObject go => go.name,
                    _ => string.Empty,
                };

                string baseName = DeriveName(type, goName);
                string fieldName = Deduplicate(baseName, usedNames);
                usedNames.Add(fieldName);

                fields.Add(new TsvrcField
                {
                    Type = type.Name,
                    Name = fieldName,
                    Namespace = type.Namespace,
                    SourceObject = obj,
                });
            }

            return fields;
        }

        private static string DeriveName(Type type, string goName)
        {
            if (type == typeof(Animator))
                return (CustomFieldName(goName) ?? goName) + "Animator";

            return CustomFieldName(goName) ?? type.Name;
        }

        // If a GameObject is named __Foo__, the field name becomes Foo instead of the type name.
        // This lets users override the generated field name directly from the scene hierarchy.
        private static string CustomFieldName(string goName)
        {
            if (goName != null
                && goName.StartsWith("__")
                && goName.EndsWith("__")
                && goName.Length > 4)
                return goName.Substring(2, goName.Length - 4);

            return null;
        }

        internal static string Deduplicate(string baseName, HashSet<string> usedNames)
        {
            string name = baseName;
            int suffix = 2;
            while (usedNames.Contains(name))
            {
                name = $"{baseName}{suffix}";
                suffix++;
            }
            return name;
        }
    }
}
#endif
