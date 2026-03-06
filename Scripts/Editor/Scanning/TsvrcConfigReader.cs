#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Finds TsvrcConfig in scene and auto-detects the TsvrcInstance subclass.
    internal static class TsvrcConfigReader
    {
        // Returns the single TsvrcConfig in the scene, or null on error.
        internal static TsvrcConfig Read()
        {
            var configs = UnityEngine.Object.FindObjectsOfType<TsvrcConfig>();

            if (configs == null || configs.Length == 0)
            {
                Debug.LogError("[TsvrcCompiler] No TsvrcConfig found in the scene.");
                return null;
            }

            if (configs.Length > 1)
            {
                var names = new StringBuilder();
                foreach (var c in configs)
                    names.Append($"\n  \u2022 {c.gameObject.name}");
                Debug.LogError("[TsvrcCompiler] Multiple TsvrcConfig found \u2014 there must be exactly one:" + names);
                return null;
            }

            return configs[0];
        }

        // Scans assemblies for a single concrete TsvrcInstance subclass.
        // Returns null if none found; errors if more than one found.
        internal static Type DetectInstanceType()
        {
            var baseType = typeof(TsvrcInstance);
            var found = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip well-known assemblies for performance.
                var asmName = assembly.FullName;
                if (asmName.StartsWith("UnityEngine") ||
                    asmName.StartsWith("UnityEditor") ||
                    asmName.StartsWith("System") ||
                    asmName.StartsWith("mscorlib") ||
                    asmName.StartsWith("netstandard"))
                    continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == baseType) continue;
                    if (!baseType.IsAssignableFrom(type)) continue;
                    if (type.IsAbstract) continue;
                    if (type.Namespace != null &&
                        type.Namespace.StartsWith("Tsvrc.Core.Compiled")) continue;

                    found.Add(type);
                }
            }

            if (found.Count == 0)
                return null; // no subclass — instance is omitted

            if (found.Count > 1)
            {
                var names = new StringBuilder();
                foreach (var t in found) names.Append($"\n  \u2022 {t.FullName}");
                Debug.LogError(
                    "[TsvrcCompiler] Multiple TsvrcInstance subclasses found \u2014 " +
                    "there must be exactly one:" + names);
                return null;
            }

            return found[0];
        }
    }
}
#endif
