#if UNITY_EDITOR
using System;
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

        internal static bool IsFactoryUsed(Type type)
        {
            var pattern = new Regex(@"\bCreate" + Regex.Escape(type.Name) + @"\s*\(");
            bool used = AnyFileMatches(pattern);

            if (!used)
                Debug.LogWarning(
                    $"[TsvrcCompiler] Create{type.Name}() is never called. " +
                    "The Create method will log an error at runtime.");

            return used;
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
