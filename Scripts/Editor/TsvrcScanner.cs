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
        public bool FactoryUsed;       // only relevant for Behaviour kind
        public bool SingletonUsed;     // only relevant for Singleton kind
        public bool IsTsvrcBehaviour;  // true if Type extends TsvrcBehaviour
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
        public TsvrcConfig SourceConfig;    // the EditorOnly TsvrcConfig in scene
        public Type InstanceType;           // TsvrcInstance or its single subclass
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
        internal static TsvrcScanResult Scan()
        {
            var configs = UnityEngine.Object.FindObjectsOfType<TsvrcConfig>();
            if (configs == null || configs.Length == 0)
            {
                Debug.LogError("[TsvrcCompiler] No TsvrcConfig found in the scene.");
                return null;
            }

            if (configs.Length > 1)
            {
                var names = new System.Text.StringBuilder();
                foreach (var c in configs) names.Append($"\n  \u2022 {c.gameObject.name}");
                Debug.LogError("[TsvrcCompiler] Multiple TsvrcConfig found \u2014 there must be exactly one:" + names);
                return null;
            }

            var config = configs[0];

            // Detect TsvrcInstance subclass (exactly 0 or 1 allowed)
            var instanceType = DetectInstanceType();
            if (instanceType == null) return null;

            var result = new TsvrcScanResult { SourceConfig = config, InstanceType = instanceType };

            var singletonGroup = new TsvrcGroup { Label = "Singletons", Kind = TsvrcGroupKind.Singleton };
            if (!ExtractEntries(config.Singletons, singletonGroup, new HashSet<string>()))
                return null;

            var behaviourGroup = new TsvrcGroup { Label = "Behaviours", Kind = TsvrcGroupKind.Behaviour };
            if (!ExtractEntries(config.Behaviours, behaviourGroup, new HashSet<string>()))
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
                    Debug.LogWarning($"[TsvrcCompiler] '{entry.FieldName}' ({entry.Type.Name}) has no '_ts.{entry.FieldName}' usage \u2014 will be hidden in inspector.");
            }

            // Resolve factory usage for behaviour entries
            foreach (var entry in behaviourGroup.Entries)
                entry.FactoryUsed = IsFactoryUsed(entry.Type);

            if (singletonGroup.Entries.Count > 0) result.Groups.Add(singletonGroup);
            if (behaviourGroup.Entries.Count > 0) result.Groups.Add(behaviourGroup);

            return result;
        }

        // ── Instance subclass detection ──────────────────────────────────────────────────────

        private static Type DetectInstanceType()
        {
            var baseType = typeof(TsvrcInstance);
            var subclasses = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = assembly.FullName;
                if (asmName.StartsWith("Unity") || asmName.StartsWith("System") ||
                    asmName.StartsWith("mscorlib") || asmName.StartsWith("Mono"))
                    continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                        if (type.BaseType == baseType)
                            subclasses.Add(type);
                }
                catch { }
            }

            if (subclasses.Count == 0) return baseType;
            if (subclasses.Count == 1) return subclasses[0];

            var list = new System.Text.StringBuilder();
            foreach (var t in subclasses) list.Append($"\n  \u2022 {t.FullName}");
            Debug.LogError("[TsvrcCompiler] Multiple TsvrcInstance subclasses found \u2014 only one is allowed:" + list);
            return null;
        }

        // ── Entry extraction ─────────────────────────────────────────────────────────────────

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
                    Debug.LogWarning($"[TsvrcCompiler] Null entry in {group.Label} \u2014 skipped.");
                    continue;
                }

                Type type = obj.GetType();

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
                        Debug.LogError($"[TsvrcCompiler] '{go.name}' ({group.Label}) has {candidates.Count} components " +
                                       $"({string.Join(", ", cnames)}). Drag the specific component \u2014 not the GameObject.");
                        return false;
                    }
                }

                string goName = obj is GameObject gobj
                    ? gobj.name
                    : obj is Component comp
                        ? comp.gameObject.name
                        : null;

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

        private static string CustomFieldName(string goName)
        {
            if (goName != null && goName.StartsWith("__") && goName.EndsWith("__") && goName.Length > 4)
                return goName.Substring(2, goName.Length - 4);
            return null;
        }

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

        private static bool IsFactoryUsed(Type type)
        {
            string assetsPath = Application.dataPath;
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
