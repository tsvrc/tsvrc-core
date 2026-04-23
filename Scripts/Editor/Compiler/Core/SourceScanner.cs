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
        // Set by TsvrcCompiler before any scan pass. Call ClearSourceCache() afterwards.
        internal static string ExcludeFolder = "Assets/CompiledTsvrc";

        // All .cs files are loaded, comment-stripped, and method-indexed once per compile
        // pass. Call ClearSourceCache() at the start of each pass so stale data is never
        // used across separate Compile() / WouldChangeSource() invocations.

        private static List<(string fileName, string src, List<(int pos, string name)> methodIndex)> _cachedSources;
        private static Dictionary<string, int> _cachedGlobalDefCounts;

        internal static void ClearSourceCache()
        {
            _cachedSources = null;
            _cachedGlobalDefCounts = null;
        }

        internal static List<(string fileName, string src, List<(int pos, string name)> methodIndex)> GetProcessedSources()
        {
            if (_cachedSources != null) return _cachedSources;

            var excludePrefixes = GetExcludePrefixes();
            _cachedSources = new List<(string, string, List<(int, string)>)>();
            foreach (var file in Directory.GetFiles(Application.dataPath, "*.cs", SearchOption.AllDirectories))
            {
                string fullPath = Path.GetFullPath(file);
                if (excludePrefixes.Any(p => fullPath.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    continue;
                string src = StripComments(File.ReadAllText(file));
                _cachedSources.Add((file, src, BuildMethodIndex(src)));
            }
            return _cachedSources;
        }

        // Keys with no matches get 0; callers do not need to check for missing keys.
        internal static Dictionary<string, int> FindCallSitesBatch(Dictionary<string, Regex> patterns)
        {
            if (patterns.Count == 0) return new Dictionary<string, int>();
            var result = new Dictionary<string, int>();
            foreach (var key in patterns.Keys)
                result[key] = 0;

            foreach (var (_, src, _) in GetProcessedSources())
                foreach (var kvp in patterns)
                    result[kvp.Key] += kvp.Value.Matches(src).Count;

            return result;
        }

        private static readonly Regex LineCommentRegex = new Regex(@"//[^\n]*", RegexOptions.Compiled);
        private static readonly Regex BlockCommentRegex = new Regex(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        internal static string StripComments(string src)
        {
            src = LineCommentRegex.Replace(src, "");
            src = BlockCommentRegex.Replace(src, "");
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

        // Returns the effective number of pool slots needed for each key.
        // A direct _ts.GetX() call counts as 1 slot.
        // A _ts.GetX() inside a helper method counts as the number of times that helper is called,
        // minimum 1 to handle VRChat callbacks invoked externally with no source-level callers.
        internal static Dictionary<string, int> CountCallSitesBatch(Dictionary<string, Regex> patterns)
        {
            if (patterns.Count == 0) return new Dictionary<string, int>();
            var result = new Dictionary<string, int>();
            foreach (var key in patterns.Keys) result[key] = 0;

            var processedSources = GetProcessedSources();
            var globalDefCounts = GetGlobalDefCounts(processedSources);

            // Pass 1: locate every _ts.GetX() call and record its enclosing method name.
            var perKeyWrappers = new Dictionary<string, List<string>>();
            foreach (var key in patterns.Keys) perKeyWrappers[key] = new List<string>();
            var wrapperNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (_, src, methodIndex) in processedSources)
            {
                foreach (var kvp in patterns)
                {
                    foreach (Match m in kvp.Value.Matches(src))
                    {
                        string wrapper = FindEnclosingMethodName(methodIndex, m.Index);
                        perKeyWrappers[kvp.Key].Add(wrapper);
                        if (wrapper != null) wrapperNames.Add(wrapper);
                    }
                }
            }

            // Pass 2: for each wrapper, count calls = total appearances minus declarations.
            // These patterns are created once and used immediately; Compiled would add JIT
            // overhead (~50-200ms each) with no benefit for single-use patterns.
            var wrapperCallCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in wrapperNames)
            {
                var callPattern = new Regex(@"\b" + Regex.Escape(name) + @"\s*\(");
                int total = 0;
                foreach (var (_, src, _) in processedSources)
                    total += callPattern.Matches(src).Count;
                int defs = globalDefCounts.TryGetValue(name, out int dc) ? dc : 0;
                wrapperCallCounts[name] = Math.Max(0, total - defs);
            }

            // Aggregate: each _ts.GetX() literal contributes max(1, callCount) slots.
            foreach (var kvp in perKeyWrappers)
            {
                int count = 0;
                foreach (var w in kvp.Value)
                    count += w == null ? 1 : Math.Max(1, wrapperCallCounts[w]);
                result[kvp.Key] = count;
            }

            return result;
        }

        // Shared by CountCallSitesBatch; cleared alongside _cachedSources.
        private static Dictionary<string, int> GetGlobalDefCounts(
            List<(string fileName, string src, List<(int pos, string name)> methodIndex)> processedSources)
        {
            if (_cachedGlobalDefCounts != null) return _cachedGlobalDefCounts;
            _cachedGlobalDefCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (_, _, methodIndex) in processedSources)
                foreach (var (_, mname) in methodIndex)
                {
                    _cachedGlobalDefCounts.TryGetValue(mname, out int c);
                    _cachedGlobalDefCounts[mname] = c + 1;
                }
            return _cachedGlobalDefCounts;
        }

        // Matches method declarations that have an explicit access modifier, captures the name.
        private static readonly Regex EnclosingMethodRegex = new Regex(
            @"\b(?:private|protected|public|internal)\s+(?:(?:static|virtual|override|abstract|async)\s+)*(?:\w+(?:<[^>]+>)?(?:\[\s*\])*\s+)+(\w+)\s*\(",
            RegexOptions.Compiled);

        // Regex.Matches returns results left-to-right, so the list is already sorted by position.
        private static List<(int pos, string name)> BuildMethodIndex(string src)
        {
            var index = new List<(int pos, string name)>();
            foreach (Match m in EnclosingMethodRegex.Matches(src))
                index.Add((m.Index, m.Groups[1].Value));
            return index;
        }

        // Binary-searches the pre-built method index for the nearest declaration before pos.
        // Returns null if no method declaration precedes pos (e.g. a field initialiser at class scope).
        private static string FindEnclosingMethodName(List<(int pos, string name)> methodIndex, int pos)
        {
            if (methodIndex.Count == 0) return null;
            int lo = 0, hi = methodIndex.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (methodIndex[mid].pos < pos) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best >= 0 ? methodIndex[best].name : null;
        }

        private static readonly Regex NamespacePattern = new Regex(@"namespace\s+([\w.]+)", RegexOptions.Compiled);

        // Returns null if no subclass is found in user code.
        internal static (string TypeName, string Namespace, string AssetPath)? FindSubclass(string baseTypeName)
        {
            // Single-use pattern: Compiled would cost ~50-200ms JIT with no reuse benefit.
            var classPattern = new Regex($@"class\s+(\w+)\s*:\s*{Regex.Escape(baseTypeName)}\b");

            foreach (var (fileName, src, _) in GetProcessedSources())
            {
                var match = classPattern.Match(src);
                if (!match.Success) continue;

                string typeName = match.Groups[1].Value;
                var nsMatch = NamespacePattern.Match(src);
                string ns = nsMatch.Success ? nsMatch.Groups[1].Value : string.Empty;
                string assetPath = "Assets" + fileName.Substring(Application.dataPath.Length).Replace('\\', '/');
                return (typeName, ns, assetPath);
            }
            return null;
        }
    }
}
#endif
