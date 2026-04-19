#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Scans <see cref="TsvrcConfig.Factories"/> and <see cref="InternalTsvrcConfig.Factories"/> and emits
    /// per-prefab private <c>GameObject</c> fields plus <c>Create{GroupName}{PrefabName}(Transform parent)</c>
    /// factory methods on <c>CompiledTsvrc</c>.
    ///
    /// <para>
    /// Factory prefabs are never placed in the scene at compile time.
    /// At runtime, each call to <c>Create*</c> instantiates a brand-new scene object.
    /// </para>
    ///
    /// <para>
    /// <b>VRChat networking limitation:</b> objects created with <c>Instantiate</c> at runtime
    /// are NOT assigned a VRChat network ID.  They cannot send or receive any VRC network
    /// events (e.g. <c>OnDeserialization</c>, <c>SendCustomNetworkEvent</c>,
    /// <c>OnPlayerJoined</c>).  If you need networked objects, register them in the Pool
    /// instead; pool slots exist in the scene before play and therefore receive stable
    /// network IDs from VRChat.
    /// </para>
    /// </summary>
    internal class FactoryModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            var usedNames = new HashSet<string>();
            var fields = new List<TsvrcField>();

            if (config.Factories != null)
                foreach (var group in config.Factories)
                {
                    if (group?.Prefabs == null) continue;
                    ScanGroup(group.GroupName, group.Prefabs, usedNames, fields, activeFieldNames: null);
                }

            var internalConfig = TsvrcCompiler.LoadInternalConfig();
            if (internalConfig?.Factories != null)
                foreach (var group in internalConfig.Factories)
                {
                    if (group?.Prefabs == null) continue;
                    ScanGroup(group.GroupName, group.Prefabs, usedNames, fields, activeFieldNames: null);
                }

            _fields = fields.OrderBy(f => f.Name).ToList();
        }

        // Strips whitespace and ensures PascalCase first letter so names are valid C# identifiers.
        private static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            // Strip __Alias__ markers.
            if (raw.StartsWith("__") && raw.EndsWith("__") && raw.Length > 4)
                raw = raw.Substring(2, raw.Length - 4);
            raw = raw.Trim();
            return raw.Length > 0 ? char.ToUpper(raw[0]) + raw.Substring(1) : string.Empty;
        }

        // Appends an integer suffix until the name is unique within usedNames.
        private static string DeriveUniqueName(string baseName, HashSet<string> usedNames)
        {
            string name = baseName;
            int suffix = 2;
            while (usedNames.Contains(name))
            {
                name = $"{baseName}{suffix}";
                suffix++;
            }
            return name;
        }

        internal override IEnumerable<string> GetWireOnlyAssetPaths()
        {
            // Read directly from config — _fields is only populated after Scan(), which is not
            // guaranteed to have run when the watcher calls this at import time.
            var config = UnityEngine.Object.FindObjectOfType<Tsvrc.Core.TsvrcConfig>();
            foreach (var path in FactoryPrefabPaths(config?.Factories))
                yield return path;
            foreach (var path in FactoryPrefabPaths(TsvrcCompiler.LoadInternalConfig()?.Factories))
                yield return path;
        }

        private static IEnumerable<string> FactoryPrefabPaths(TsvrcFactoryGroup[] groups)
        {
            if (groups == null) yield break;
            foreach (var group in groups)
            {
                if (group?.Prefabs == null) continue;
                foreach (var obj in group.Prefabs)
                {
                    if (obj == null) continue;
                    var path = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(path))
                        yield return path;
                }
            }
        }

        /// <summary>
        /// Wire-only scan: uses reflection on the compiled type to include only factory fields that
        /// were generated during the last full compile (i.e. had call sites at that time).
        /// </summary>
        internal override void ScanForWire(TsvrcConfig config, Type compiledType)
        {
            var activeFieldNames = new HashSet<string>(
                compiledType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(f => f.Name));

            var usedNames = new HashSet<string>();
            var fields = new List<TsvrcField>();

            if (config.Factories != null)
                foreach (var group in config.Factories)
                {
                    if (group?.Prefabs == null) continue;
                    ScanGroup(group.GroupName, group.Prefabs, usedNames, fields, activeFieldNames);
                }

            var internalConfig = TsvrcCompiler.LoadInternalConfig();
            if (internalConfig?.Factories != null)
                foreach (var group in internalConfig.Factories)
                {
                    if (group?.Prefabs == null) continue;
                    ScanGroup(group.GroupName, group.Prefabs, usedNames, fields, activeFieldNames);
                }

            _fields = fields.OrderBy(f => f.Name).ToList();
        }

        /// <summary>
        /// Scans a factory group and adds fields to <paramref name="fields"/>.
        /// <para>
        /// When <paramref name="activeFieldNames"/> is <c>null</c> (full compile): all prefabs are
        /// included and <c>CallSites</c> is populated by scanning user source files.
        /// </para>
        /// <para>
        /// When <paramref name="activeFieldNames"/> is provided (wire-only): only prefabs whose
        /// serialized field already exists in the compiled type are included, and
        /// <c>CallSites</c> is set to <c>null</c> to signal to <see cref="Wire"/> that the field
        /// must be wired unconditionally (it was active at the last full compile).
        /// </para>
        /// </summary>
        private void ScanGroup(string groupName, UnityEngine.Object[] prefabs, HashSet<string> usedNames, List<TsvrcField> fields, HashSet<string> activeFieldNames)
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

                string name = DeriveUniqueName(groupPrefix + Sanitize(prefab.name), usedNames);
                usedNames.Add(name);

                // Wire-only: skip fields not present in the compiled type.
                if (activeFieldNames != null && !activeFieldNames.Contains(FieldName(name))) continue;

                fields.Add(new TsvrcField
                {
                    Name = name,
                    Type = type != null ? type.Name : "GameObject",
                    Namespace = type?.Namespace ?? string.Empty,
                    SourceObject = (UnityEngine.Object)behaviour ?? prefab,
                    // Full compile: scan call sites. Wire-only (activeFieldNames != null): null
                    // signals Wire() to include this field unconditionally.
                    CallSites = activeFieldNames == null
                        ? SourceScanner.FindCallSites(new Regex(@"\b_ts\s*\.\s*Create" + Regex.Escape(name) + @"\s*\("))
                        : null,
                });
            }
        }

        internal override IEnumerable<string> GetUsings()
            => _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            if (_fields.Count == 0) return;

            w.Region("Factories");
            foreach (var field in _fields)
                if (field.CallSites?.Count > 0)
                    w.Line($"[HideInInspector] [SerializeField] private GameObject {FieldName(field.Name)};");
            w.EndRegion();
        }

        internal override void WriteMethods(CsWriter w)
        {
            if (_fields.Count == 0) return;

            w.Region("Factory Methods");
            foreach (var field in _fields)
            {
                w.Summary(
                    $"Instantiates a new <see cref=\"{field.Type}\"/> from its prefab under the given parent. " +
                    $"NETWORKING WARNING: runtime-instantiated objects are NOT assigned a VRChat network ID " +
                    $"and cannot send or receive VRC network events " +
                    $"(OnDeserialization, SendCustomNetworkEvent, OnPlayerJoined, etc.). " +
                    $"For networked objects register them in the Pool instead.");

                using (w.Method($"public {field.Type} Create{field.Name}(Transform parent)"))
                {
                    if (field.CallSites?.Count == 0)
                    {
                        w.Line($"Debug.LogError(\"{TsvrcCodeGen.NullFieldMessage($"Create{field.Name}")}\");");
                        w.Line("return null;");
                        continue;
                    }

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
                        if (field.SourceObject is TsvrcBehaviour)
                            w.Line("if (instance != null) instance.TsConstruct(this);");
                        w.Line("return instance;");
                    }
                }
            }
            w.EndRegion();
        }

        internal override void Wire(SerializedObject target)
        {
            if (_fields.Count == 0) return;

            var compiled = target.targetObject as Component;
            if (compiled == null) return;

            // Find or create the Factories container under the CompiledTsvrc GameObject.
            // Stale instances from a previous compile are removed first; the parent GameObject
            // itself is recreated clean each time so there is no leftover state.
            var existing = compiled.transform.Find("Factories");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var factoriesGo = new GameObject("Factories");
            Undo.RegisterCreatedObjectUndo(factoriesGo, "Create Factories Container");
            factoriesGo.transform.SetParent(compiled.transform, false);

            foreach (var field in _fields)
            {
                // Full-compile path: CallSites is non-null — skip fields with no call sites (they get stubs).
                // Wire-only path: CallSites is null — field was filtered via reflection and must be wired.
                if (field.CallSites?.Count == 0) continue;

                var prop = target.FindProperty(FieldName(field.Name));
                if (prop == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Factory property '{FieldName(field.Name)}' not found on CompiledTsvrc.");
                    continue;
                }

                var prefabAsset = field.SourceObject is Component c ? c.gameObject : (GameObject)field.SourceObject;

                // Instantiate as a scene object so Unity strips EditorOnly children at build time.
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, factoriesGo.transform);
                instance.name = field.Name;
                instance.SetActive(false);

                prop.objectReferenceValue = instance;
            }
        }

        private static string FieldName(string name) => $"_factory{name}";
    }
}
#endif
