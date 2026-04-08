#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Scans all <see cref="TsvrcFactoryGroup"/> children of <see cref="TsvrcConfig"/> and emits
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
            _fields.Clear();

            var groups = config.GetComponentsInChildren<TsvrcFactoryGroup>();
            if (groups.Length == 0) return;

            var usedNames = new HashSet<string>();
            var fields = new List<TsvrcField>();

            foreach (var group in groups)
            {
                if (group.Prefabs == null) continue;

                string groupPrefix = string.IsNullOrEmpty(group.GroupName)
                    ? string.Empty
                    : Sanitize(group.GroupName);

                foreach (var obj in group.Prefabs)
                {
                    if (obj == null) continue;

                    var prefab = obj is Component c ? c.gameObject : obj as GameObject;
                    if (prefab == null) continue;

                    var behaviour = prefab.GetComponent<TsvrcBehaviour>();
                    var type = behaviour != null ? behaviour.GetType() : null;

                    string name = DeriveUniqueName(groupPrefix + Sanitize(prefab.name), usedNames);
                    usedNames.Add(name);

                    var pattern = new Regex(@"\b_ts\s*\.\s*Create" + Regex.Escape(name) + @"\s*\(");

                    fields.Add(new TsvrcField
                    {
                        Name = name,
                        Type = type != null ? type.Name : "GameObject",
                        Namespace = type?.Namespace ?? string.Empty,
                        SourceObject = (UnityEngine.Object)behaviour ?? prefab,
                        CallSites = SourceScanner.FindCallSites(pattern),
                    });
                }
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

        internal override IEnumerable<string> GetUsings()
            => _fields.Select(f => f.Namespace).Where(n => !string.IsNullOrEmpty(n));

        internal override void WriteFields(CsWriter w)
        {
            if (_fields.Count == 0) return;

            w.Region("Factories");
            foreach (var field in _fields)
                if (field.CallSites.Count > 0)
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
                    if (field.CallSites.Count == 0)
                    {
                        w.Line($"Debug.LogError(\"{TsvrcCodeGen.NullFieldMessage($"Create{field.Name}")}\");");
                        w.Line("return null;");
                        continue;
                    }

                    if (field.Type == "GameObject")
                    {
                        w.Line($"return (GameObject)Instantiate({FieldName(field.Name)}, parent);");
                    }
                    else
                    {
                        w.Line($"var go = (GameObject)Instantiate({FieldName(field.Name)}, parent);");
                        w.Line("if (go == null) return null;");
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
            foreach (var field in _fields)
            {
                if (field.CallSites.Count == 0) continue;

                var prop = target.FindProperty(FieldName(field.Name));
                if (prop == null)
                {
                    Debug.LogWarning($"[TsvrcWirer] Factory property '{FieldName(field.Name)}' not found on CompiledTsvrc.");
                    continue;
                }

                prop.objectReferenceValue = field.SourceObject is Component c
                    ? c.gameObject
                    : (GameObject)field.SourceObject;
            }
        }

        private static string FieldName(string name) => $"_factory{name}";
    }
}
#endif
