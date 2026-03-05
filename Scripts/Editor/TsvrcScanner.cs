#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal enum TsvrcGroupKind { Singleton, Behaviour }

    internal class TsvrcEntry
    {
        public Type Type;
        public string FieldName;
        public bool FactoryUsed; // only relevant for Behaviour kind
    }

    internal class TsvrcGroup
    {
        public string Label;
        public TsvrcGroupKind Kind;
        public List<TsvrcEntry> Entries = new List<TsvrcEntry>();
    }

    internal class TsvrcScanResult
    {
        public List<TsvrcGroup> Groups = new List<TsvrcGroup>();

        public int TotalEntries()
        {
            int count = 0;
            foreach (var g in Groups) count += g.Entries.Count;
            return count;
        }
    }

    internal static class TsvrcScanner
    {
        /// <summary>
        /// Scans the active scene for a TsvrcInstance and resolves all registered entry groups.
        /// Returns null and logs an error if validation fails.
        /// </summary>
        internal static TsvrcScanResult Scan(string generatedFilePath)
        {
            var instances = UnityEngine.Object.FindObjectsOfType<TsvrcInstance>();
            if (instances == null || instances.Length == 0)
            {
                Debug.LogError("[TsvrcCompiler] No TsvrcInstance found in the scene.");
                return null;
            }

            if (instances.Length > 1)
            {
                var names = new System.Text.StringBuilder();
                foreach (var i in instances)
                    names.Append($"\n  • {i.gameObject.name}");
                Debug.LogError("[TsvrcCompiler] Multiple TsvrcInstance found — there must be exactly one:" + names);
                return null;
            }

            var instance = instances[0];
            var result = new TsvrcScanResult();
            var usedNames = new HashSet<string>();

            var singletonGroup = new TsvrcGroup { Label = "Singletons", Kind = TsvrcGroupKind.Singleton };
            if (!ExtractEntries(instance.Singletons, singletonGroup, usedNames))
                return null;

            var behaviourGroup = new TsvrcGroup { Label = "Behaviours", Kind = TsvrcGroupKind.Behaviour };
            if (!ExtractEntries(instance.Behaviours, behaviourGroup, usedNames))
                return null;

            if (singletonGroup.Entries.Count == 0 && behaviourGroup.Entries.Count == 0)
            {
                Debug.LogWarning("[TsvrcCompiler] All arrays are empty. Nothing to generate.");
                return null;
            }

            // Resolve factory usage for behaviour entries
            foreach (var entry in behaviourGroup.Entries)
                entry.FactoryUsed = IsFactoryUsed(entry.Type, generatedFilePath);

            if (singletonGroup.Entries.Count > 0) result.Groups.Add(singletonGroup);
            if (behaviourGroup.Entries.Count > 0) result.Groups.Add(behaviourGroup);

            return result;
        }

        // ── Type resolution ──────────────────────────────────────────────────────────────────

        private static bool ExtractEntries(
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
                    Debug.LogWarning($"[TsvrcCompiler] Null entry in {group.Label} — skipped.");
                    continue;
                }

                Type type = obj.GetType();

                // Auto-resolve GameObjects: find the one meaningful component on the object.
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
                        var names = new List<string>();
                        foreach (var c in candidates) names.Add(c.GetType().Name);
                        Debug.LogError($"[TsvrcCompiler] '{go.name}' ({group.Label}) has {candidates.Count} components " +
                                       $"({string.Join(", ", names)}). Drag the specific component — not the GameObject.");
                        return false;
                    }
                }

                // De-duplicate field names across all groups
                string baseName = type.Name;
                string fieldName = baseName;
                int suffix = 2;
                while (usedNames.Contains(fieldName))
                    fieldName = baseName + (suffix++);

                usedNames.Add(fieldName);
                group.Entries.Add(new TsvrcEntry { Type = type, FieldName = fieldName });
            }

            return true;
        }

        // ── Usage detection ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if <c>Create{type.Name}()</c> is called anywhere in the project
        /// except the generated output file.
        /// </summary>
        private static bool IsFactoryUsed(Type type, string generatedFilePath)
        {
            string assetsPath = Application.dataPath;
            string normalizedGenerated = Path.GetFullPath(generatedFilePath);
            var callPattern = new Regex(@"\bCreate" + Regex.Escape(type.Name) + @"\s*\(");

            foreach (var file in Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFullPath(file), normalizedGenerated, StringComparison.OrdinalIgnoreCase))
                    continue;

                string src = File.ReadAllText(file);
                src = Regex.Replace(src, @"//[^\n]*", "");
                src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);

                if (callPattern.IsMatch(src))
                    return true;
            }

            Debug.LogWarning($"[TsvrcCompiler] Create{type.Name}() is never called. The Create method will log an error at runtime.");
            return false;
        }
    }
}
#endif
