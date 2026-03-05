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
        public bool FactoryUsed;      // only relevant for Behaviour kind
        public bool SingletonUsed;    // only relevant for Singleton kind
        public bool IsTsvrcBehaviour; // true if Type extends TsvrcBehaviour
        public UnityEngine.Object SceneObject; // actual scene reference for wiring
    }

    internal class TsvrcGroup
    {
        public string Label;
        public TsvrcGroupKind Kind;
        public List<TsvrcEntry> Entries = new List<TsvrcEntry>();
    }

    internal class TsvrcScanResult
    {
        public TsvrcInstance SourceInstance; // the EditorOnly TsvrcInstance in scene
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
        internal static TsvrcScanResult Scan()
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
            var result = new TsvrcScanResult { SourceInstance = instance };

            // Each group lives in a separate generated class, so they have independent name spaces.
            var singletonGroup = new TsvrcGroup { Label = "Singletons", Kind = TsvrcGroupKind.Singleton };
            if (!ExtractEntries(instance.Singletons, singletonGroup, new HashSet<string>()))
                return null;

            var behaviourGroup = new TsvrcGroup { Label = "Behaviours", Kind = TsvrcGroupKind.Behaviour };
            if (!ExtractEntries(instance.Behaviours, behaviourGroup, new HashSet<string>()))
                return null;

            if (singletonGroup.Entries.Count == 0 && behaviourGroup.Entries.Count == 0)
            {
                Debug.LogWarning("[TsvrcCompiler] All arrays are empty. Nothing to generate.");
                return null;
            }

            // Mark singletons: used = has _ts.FieldName reference in code
            foreach (var entry in singletonGroup.Entries)
            {
                entry.SingletonUsed = IsSingletonUsed(entry.FieldName);
                if (!entry.SingletonUsed)
                    Debug.LogWarning($"[TsvrcCompiler] '{entry.FieldName}' ({entry.Type.Name}) has no '_ts.{entry.FieldName}' usage — will be hidden in inspector.");
            }

            // Resolve factory usage for behaviour entries
            foreach (var entry in behaviourGroup.Entries)
                entry.FactoryUsed = IsFactoryUsed(entry.Type);

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

                // De-duplicate field names across all groups.
                // If the GameObject is named __CustomName__, use that as the field name.
                // For Animator: always derive from the GameObject name (e.g. "Player" + "Animator" → "PlayerAnimator").
                string goName = obj is GameObject g ? g.name : obj is Component c2 ? c2.gameObject.name : null;
                string baseName;
                if (type == typeof(UnityEngine.Animator))
                    baseName = (CustomFieldName(goName) ?? goName) + "Animator";
                else
                    baseName = CustomFieldName(goName) ?? type.Name;
                string fieldName = baseName;
                int suffix = 2;
                while (usedNames.Contains(fieldName))
                    fieldName = baseName + (suffix++);

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

        // ── Usage detection ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// If <paramref name="goName"/> matches <c>__Name__</c>, returns <c>Name</c>.
        /// Otherwise returns null, signalling that the type name should be used instead.
        /// </summary>
        private static string CustomFieldName(string goName)
        {
            if (goName != null && goName.StartsWith("__") && goName.EndsWith("__") && goName.Length > 4)
                return goName.Substring(2, goName.Length - 4);
            return null;
        }

        /// <summary>
        /// Returns true if <c>_ts.FieldName</c> is referenced anywhere in the project
        /// except the generated output folder.
        /// </summary>
        private static bool IsSingletonUsed(string fieldName)
        {
            string assetsPath = Application.dataPath;
            string generatedFolder = Path.GetFullPath(Path.Combine(assetsPath, "TsvrcGenerated"));
            var pattern = new Regex(@"\b_ts\s*\.\s*" + Regex.Escape(fieldName) + @"\b");

            foreach (var file in Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFullPath(file).StartsWith(generatedFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                string src = File.ReadAllText(file);
                src = Regex.Replace(src, @"//[^\n]*", "");
                src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);

                if (pattern.IsMatch(src))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if <c>Create{type.Name}()</c> is called anywhere in the project
        /// except the generated output file.
        /// </summary>
        private static bool IsFactoryUsed(Type type)
        {
            string assetsPath = Application.dataPath;
            // All generated files live under Assets/TsvrcGenerated — exclude entire folder.
            string generatedFolder = Path.GetFullPath(Path.Combine(assetsPath, "TsvrcGenerated"));
            var callPattern = new Regex(@"\bCreate" + Regex.Escape(type.Name) + @"\s*\(");

            foreach (var file in Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFullPath(file).StartsWith(generatedFolder, StringComparison.OrdinalIgnoreCase))
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
