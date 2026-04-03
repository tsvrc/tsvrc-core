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
    /// Scans <see cref="TsvrcConfig.FactoryPrefabs"/> and emits per-prefab private
    /// <c>GameObject</c> fields plus <c>Create{Name}(Transform parent)</c> factory methods
    /// on <c>CompiledTsvrc</c>.
    ///
    /// <para>
    /// Factory prefabs are never placed in the scene at compile time.
    /// At runtime, each call to <c>Create{Name}</c> instantiates a brand-new scene object.
    /// </para>
    ///
    /// <para>
    /// <b>VRChat networking limitation:</b> objects created with <c>Instantiate</c> at runtime
    /// are NOT assigned a VRChat network ID.  They cannot send or receive any VRC network
    /// events (e.g. <c>OnDeserialization</c>, <c>SendCustomNetworkEvent</c>,
    /// <c>OnPlayerJoined</c>).  If you need networked objects, register them in the Pool
    /// instead, pool slots exist in the scene before play and therefore receive stable
    /// network IDs from VRChat.
    /// </para>
    /// </summary>
    internal class FactoryModule : TsvrcModule
    {
        private List<TsvrcField> _fields = new List<TsvrcField>();

        internal override void Scan(TsvrcConfig config)
        {
            if (config.FactoryPrefabs == null || config.FactoryPrefabs.Length == 0)
            {
                _fields.Clear();
                return;
            }

            var usedNames = new HashSet<string>();
            var fields = new List<TsvrcField>();

            foreach (var prefab in config.FactoryPrefabs)
            {
                if (prefab == null) continue;

                var behaviour = prefab.GetComponent<TsvrcBehaviour>();
                var type = behaviour != null ? behaviour.GetType() : null;

                string name = DeriveUniqueName(prefab.name, usedNames);
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

            _fields = fields.OrderBy(f => f.Name).ToList();
        }

        // Applies the __Alias__ convention, PascalCase, and deduplicates against already-used names.
        private static string DeriveUniqueName(string prefabName, HashSet<string> usedNames)
        {
            string baseName = prefabName;
            if (baseName.StartsWith("__") && baseName.EndsWith("__") && baseName.Length > 4)
                baseName = baseName.Substring(2, baseName.Length - 4);
            if (baseName.Length > 0)
                baseName = char.ToUpper(baseName[0]) + baseName.Substring(1);

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
                    w.Line($"[SerializeField] private GameObject {FieldName(field.Name)};");
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
                        w.Line($"var instance = ({field.Type})go.GetComponent(typeof({field.Type}));");
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
