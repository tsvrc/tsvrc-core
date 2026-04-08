#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class TsvrcResolver
    {
        internal static HashSet<TsvrcField> Resolve(
            HashSet<UnityEngine.Object> objects,
            HashSet<string> usedNames
        )
        {
            var fields = new HashSet<TsvrcField>();

            if (objects == null || objects.Count == 0)
                return fields;

            foreach (var obj in objects)
            {
                if (obj == null) { Debug.LogWarning("[TsvrcResolver] Null entry in config — remove the missing-script slot from TsvrcConfig and recompile."); continue; }
                Type type = obj.GetType();

                string goName = "";
                if (obj is GameObject go)
                    goName = go.name;
                else if (obj is Component component)
                    goName = component.gameObject.name;

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
            {
                name = $"{baseName}{suffix}";
                suffix++;
            }
            return name;
        }
    }
}
#endif
