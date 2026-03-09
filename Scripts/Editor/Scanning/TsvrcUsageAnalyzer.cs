#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Regex-based usage detection over .cs files in Assets/ (excludes TsvrcGenerated/).
    internal static class TsvrcUsageAnalyzer
    {
        private static readonly string GeneratedFolder =
            Path.GetFullPath(Path.Combine(Application.dataPath, "TsvrcGenerated"));

        internal static bool IsSingletonUsed(string fieldName)
        {
            var pattern = new Regex(@"\b_ts\s*\.\s*" + Regex.Escape(fieldName) + @"\b");
            return AnyFileMatches(pattern);
        }

        // Returns one TsvrcCallSite per physical call site of Get{TypeName}() in user code.
        // Call site count determines the size of the pre-allocated pool.
        internal static List<TsvrcCallSite> FindGetCallSites(Type type)
        {
            var pattern = new Regex(@"\bGet" + Regex.Escape(type.Name) + @"\s*\(");
            var callSites = new List<TsvrcCallSite>();

            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFullPath(file).StartsWith(GeneratedFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                string src = File.ReadAllText(file);
                src = StripComments(src);

                var matches = pattern.Matches(src);
                string className = Path.GetFileNameWithoutExtension(file);
                for (int i = 0; i < matches.Count; i++)
                    callSites.Add(new TsvrcCallSite { ClassName = className, FileName = file });
            }

            if (callSites.Count == 0)
                Debug.LogWarning(
                    $"[TsvrcCompiler] Get{type.Name}() is never called. " +
                    "The Get method will log an error at runtime.");

            return callSites;
        }

        private static bool AnyFileMatches(Regex pattern)
        {
            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFullPath(file).StartsWith(GeneratedFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                string src = File.ReadAllText(file);
                src = StripComments(src);

                if (pattern.IsMatch(src))
                    return true;
            }
            return false;
        }

        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"//[^\n]*", "");
            src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return src;
        }
    }
}
#endif
