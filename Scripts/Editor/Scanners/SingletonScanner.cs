#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class SingletonScanner
    {
        private const string GeneratedFolder = "Assets/CompiledTsvrc";

        internal static ScanResult Scan(TsvrcConfig config)
        {
            ScanResult result = new ScanResult();

            var singletons = config.Singletons;
            var internalSingletons = config.InternalTsvrcConfig.Singletons;

            var usedNames = new HashSet<string>();
            var objects = singletons.Union(internalSingletons).ToHashSet();

            var fields = TsvrcResolver.Resolve(objects, usedNames);

            foreach (var field in fields)
            {
                field.CallSites = GetCalls(field.Name);
            }

            result.Fields = fields;

            var builder = new SingletonBuilder();
            result.Builder = builder;
            return result;
        }

        public static List<TsvrcCallSite> GetCalls(string field)
        {
            var callSites = new List<TsvrcCallSite>();

            var pattern = new Regex(@"\b_ts\s*\.\s*" + Regex.Escape(field) + @"\b");

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

            return callSites;
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