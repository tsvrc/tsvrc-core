#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Emits per-prefab fields and Create*() methods for each factory group.
    // Prefabs are never placed in the scene at compile time; Create*() instantiates them at runtime.
    // Runtime-instantiated objects get no VRChat network ID, so they cannot use any VRC network events.
    internal class FactoryModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
            => _fields = BuildFields(config, activeFieldNames: null);

        // Only wire factory fields that exist in the compiled type. Fields added after
        // the last full compile are skipped until the user recompiles.
        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            var activeFieldNames = new HashSet<string>(
                compiledType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => f.Name));

            _fields = BuildFields(config, activeFieldNames);
        }

        private List<TsvrcField> BuildFields(TsvrcConfig config, HashSet<string> activeFieldNames)
        {
            var usedNames = new HashSet<string>();
            var fields = new List<TsvrcField>();

            foreach (var group in AllFactoryGroups(config))
                ScanGroup(group.GroupName, group.Prefabs, usedNames, fields, activeFieldNames);

            var sorted = fields.OrderBy(f => f.Name).ToList();

            // Full compile only: scan all user .cs files once for all factory call sites.
            // Wire-only path leaves CallSiteCount as 0 (WireAlways drives wiring instead).
            if (activeFieldNames == null)
            {
                var patterns = sorted.ToDictionary(
                    f => f.Name,
                    f => new Regex(@"\b_ts\s*\.\s*Create" + Regex.Escape(f.Name) + @"\s*\(", RegexOptions.Compiled));
                var callSiteMap = SourceScanner.FindCallSitesBatch(patterns);
                foreach (var field in sorted)
                    field.CallSiteCount = callSiteMap[field.Name];
            }

            return sorted;
        }

        private static IEnumerable<TsvrcFactoryGroup> AllFactoryGroups(TsvrcConfig config)
        {
            if (config?.Factories != null)
                foreach (var group in config.Factories)
                    if (group?.Prefabs != null) yield return group;

            var internalConfig = TsvrcCompiler.LoadInternalConfig();
            if (internalConfig?.Factories != null)
                foreach (var group in internalConfig.Factories)
                    if (group?.Prefabs != null) yield return group;
        }

        // Full compile (activeFieldNames == null): all prefabs are included and CallSiteCount is
        // populated from user source files.
        // Wire-only (activeFieldNames provided): only prefabs whose field already exists in the
        // compiled type are included, and WireAlways is set so Wire() skips the call-site check.
        private static void ScanGroup(
            string groupName,
            UnityEngine.Object[] prefabs,
            HashSet<string> usedNames,
            List<TsvrcField> fields,
            HashSet<string> activeFieldNames)
        {
            string groupPrefix = string.IsNullOrEmpty(groupName) ? string.Empty : Sanitize(groupName);

            foreach (var obj in prefabs)
            {
                if (obj == null) continue;

                var prefab = obj is Component c ? c.gameObject : obj as GameObject;
                if (prefab == null) continue;

                if (!EditorUtility.IsPersistent(prefab))
                {
                    if (activeFieldNames == null) // Only warn during full compile.
                        Debug.LogWarning($"[Tsvrc] Factory entry '{prefab.name}' is a scene object, not a prefab asset. Drag a prefab asset from the Project window instead. Skipping.");
                    continue;
                }

                var behaviour = prefab.GetComponent<TsvrcBehaviour>();
                var type = behaviour != null ? behaviour.GetType() : null;

                string name = TsvrcResolver.Deduplicate(groupPrefix + Sanitize(prefab.name), usedNames);
                usedNames.Add(name);

                // Wire-only: skip fields not present in the compiled type.
                if (activeFieldNames != null && !activeFieldNames.Contains(FieldName(name))) continue;

                fields.Add(new TsvrcField
                {
                    Name = name,
                    Type = type != null ? type.Name : "GameObject",
                    Namespace = type?.Namespace ?? string.Empty,
                    SourceObject = (UnityEngine.Object)behaviour ?? prefab,
                    WireAlways = activeFieldNames != null,
                    // CallSiteCount assigned by BuildFields via batch scan (full compile); 0 on wire-only path.
                });
            }
        }

        internal override IEnumerable<string> GetUsings()
            => _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        private const string NetworkingWarning =
            "NETWORKING WARNING: runtime-instantiated objects are NOT assigned a VRChat network ID " +
            "and cannot send or receive VRC network events " +
            "(OnDeserialization, SendCustomNetworkEvent, OnPlayerJoined, etc.). " +
            "For networked objects register them in the Pool instead.";

        internal override void WriteFields(CsWriter w)
        {
            var active = _fields.Where(f => f.CallSiteCount > 0).ToList();
            if (active.Count == 0) return;

            w.Region("Factories");
            foreach (var field in active)
                w.Line($"[HideInInspector] [SerializeField] private GameObject {FieldName(field.Name)};");
            w.EndRegion();
        }

        internal override void WriteMethods(CsWriter w)
        {
            if (_fields.Count == 0) return;

            w.Region("Factory Methods");
            foreach (var field in _fields)
            {
                if (field.CallSiteCount == 0)
                {
                    w.Summary(TsvrcCodeGen.StubSummary($"Create{field.Name}"));
                    using (w.Method($"public {field.Type} Create{field.Name}(Transform parent)"))
                    {
                        w.Line($"Debug.LogError(\"{TsvrcCodeGen.NullFieldMessage($"Create{field.Name}")}\");");
                        w.Line("return null;");
                    }
                }
                else
                {
                    w.Summary($"Instantiates a new <see cref=\"{field.Type}\"/> from its prefab under the given parent. {NetworkingWarning}");
                    using (w.Method($"public {field.Type} Create{field.Name}(Transform parent)"))
                    {
                        w.Line($"var go = (GameObject)Instantiate({FieldName(field.Name)}, parent);");
                        w.Line("if (go == null) return null;");
                        w.Line("go.SetActive(true);");
                        if (field.Type == "GameObject")
                        {
                            w.Line("return go;");
                        }
                        else
                        {
                            w.Line($"var instance = go.GetComponent<{field.Type}>();");
                            w.Line("if (instance != null) instance.TsConstruct(this);");
                            w.Line("return instance;");
                        }
                    }
                }
            }
            w.EndRegion();
        }

        internal override void Wire(SerializedObject target)
        {
            var fieldsToWire = _fields.Where(f => f.WireAlways || f.CallSiteCount > 0).ToList();
            if (fieldsToWire.Count == 0) return;

            var compiled = target.targetObject as Component;
            if (compiled == null) return;

            // Find or create the Factories container under the CompiledTsvrc GameObject.
            // Stale instances from a previous compile are removed first so there is no leftover state.
            var existing = compiled.transform.Find("Factories");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            // Create the container lazily so all-invalid entries don't leave an empty GameObject in the scene.
            GameObject factoriesGo = null;

            foreach (var field in fieldsToWire)
            {
                var prop = target.FindProperty(FieldName(field.Name));
                if (prop == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Factory property '{FieldName(field.Name)}' not found on CompiledTsvrc.");
                    continue;
                }

                var prefabAsset = field.SourceObject is Component c ? c.gameObject : (GameObject)field.SourceObject;

                if (factoriesGo == null)
                {
                    factoriesGo = new GameObject("Factories");
                    Undo.RegisterCreatedObjectUndo(factoriesGo, "Create Factories Container");
                    factoriesGo.transform.SetParent(compiled.transform, false);
                }

                // Instantiate as a scene object so Unity strips EditorOnly children at build time.
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, factoriesGo.transform);
                if (instance == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Failed to instantiate factory prefab '{field.Name}'. The prefab asset may be missing or corrupted.");
                    continue;
                }

                instance.name = field.Name;
                instance.SetActive(false);

                prop.objectReferenceValue = instance;
            }
        }

        // Strips __Alias__ markers, splits on non-alphanumeric separators, PascalCases each word,
        // and prepends '_' if the result starts with a digit.
        internal static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            // Strip __Alias__ markers.
            if (raw.StartsWith("__") && raw.EndsWith("__") && raw.Length > 4)
                raw = raw.Substring(2, raw.Length - 4);

            // Split on non-alphanumeric characters, PascalCase each word.
            var sb = new StringBuilder();
            bool capitalizeNext = true;
            foreach (char ch in raw)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(capitalizeNext ? char.ToUpper(ch) : ch);
                    capitalizeNext = false;
                }
                else
                {
                    capitalizeNext = true;
                }
            }

            if (sb.Length == 0) return string.Empty;

            // C# identifiers cannot start with a digit.
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');

            return sb.ToString();
        }

        private static string FieldName(string name) => $"_factory{name}";
    }

}
#endif
