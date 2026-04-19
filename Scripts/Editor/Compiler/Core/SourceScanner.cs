#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class SourceScanner
    {
        // Set by TsvrcCompiler before any scan pass.
        internal static string ExcludeFolder = "Assets/CompiledTsvrc";

        // Scans all user .cs files once and distributes call sites to all provided patterns.
        // Returns one list per key; keys with no matches get an empty list.
        internal static Dictionary<string, List<TsvrcCallSite>> FindCallSitesBatch(Dictionary<string, Regex> patterns)
        {
            var result = new Dictionary<string, List<TsvrcCallSite>>();
            foreach (var key in patterns.Keys)
                result[key] = new List<TsvrcCallSite>();

            var excludePrefixes = GetExcludePrefixes();

            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string fullPath = Path.GetFullPath(file);
                if (excludePrefixes.Any(p => fullPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string src = StripComments(File.ReadAllText(file));
                string className = Path.GetFileNameWithoutExtension(file);
                var callSite = new TsvrcCallSite { ClassName = className, FileName = file };

                foreach (var kvp in patterns)
                {
                    var matches = kvp.Value.Matches(src);
                    for (int i = 0; i < matches.Count; i++)
                        result[kvp.Key].Add(callSite);
                }
            }

            return result;
        }

        internal static string StripComments(string src)
        {
            src = Regex.Replace(src, @"//[^\n]*", "");
            src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return src;
        }

        private static List<string> GetExcludePrefixes()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return new List<string>
            {
                Path.GetFullPath(Path.Combine(projectRoot, ExcludeFolder)),
                Path.GetFullPath(Path.Combine(projectRoot, "Assets/Tsvrc")),
            };
        }

        // Finds the first class in user code that directly inherits baseTypeName.
        // Returns (TypeName, Namespace, UnityAssetPath) or null if not found.
        internal static (string TypeName, string Namespace, string AssetPath)? FindSubclass(string baseTypeName)
        {
            var classPattern = new Regex($@"class\s+(\w+)\s*:\s*{Regex.Escape(baseTypeName)}\b");
            var nsPattern = new Regex(@"namespace\s+([\w.]+)");
            var excludePrefixes = GetExcludePrefixes();

            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string fullPath = Path.GetFullPath(file);
                if (excludePrefixes.Any(p => fullPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string src = StripComments(File.ReadAllText(file));
                var match = classPattern.Match(src);
                if (!match.Success) continue;

                string typeName = match.Groups[1].Value;
                var nsMatch = nsPattern.Match(src);
                string ns = nsMatch.Success ? nsMatch.Groups[1].Value : string.Empty;
                string assetPath = "Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/');
                return (typeName, ns, assetPath);
            }
            return null;
        }
    }
}
#endif
