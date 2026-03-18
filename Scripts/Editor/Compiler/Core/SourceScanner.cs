#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class SourceScanner
    {
        // Set by TsvrcCompiler before any scan pass.
        internal static string ExcludeFolder = "Assets/CompiledTsvrc";

        internal static List<TsvrcCallSite> FindCallSites(Regex pattern)
        {
            var results = new List<TsvrcCallSite>();
            string excludePrefix = GetExcludePrefix();

            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFullPath(file).StartsWith(excludePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string src = StripComments(File.ReadAllText(file));
                var matches = pattern.Matches(src);
                if (matches.Count == 0) continue;

                string className = Path.GetFileNameWithoutExtension(file);
                for (int i = 0; i < matches.Count; i++)
                    results.Add(new TsvrcCallSite { ClassName = className, FileName = file });
            }

            return results;
        }

        internal static string StripComments(string src)
        {
            src = Regex.Replace(src, @"//[^\n]*", "");
            src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return src;
        }

        private static string GetExcludePrefix()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot, ExcludeFolder));
        }

        // Finds the first class in user code that directly inherits baseTypeName.
        // Returns (TypeName, Namespace, UnityAssetPath) or null if not found.
        internal static (string TypeName, string Namespace, string AssetPath)? FindSubclass(string baseTypeName)
        {
            var classPattern = new Regex($@"class\s+(\w+)\s*:\s*{Regex.Escape(baseTypeName)}\b");
            var nsPattern = new Regex(@"namespace\s+([\w.]+)");
            string excludePrefix = GetExcludePrefix();

            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                if (Path.GetFullPath(file).StartsWith(excludePrefix, StringComparison.OrdinalIgnoreCase))
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
