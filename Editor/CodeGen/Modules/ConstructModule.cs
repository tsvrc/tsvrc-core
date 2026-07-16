#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Core;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates a _construct{Name} field per TsBehaviour listed in TsConfig.Constructs,
    // and wires each directly from the scene. At runtime, _TsConstructStart() calls
    // TsConstruct(this) on every wired behaviour in field-name order.
    internal class ConstructModule : TsModule
    {
        private List<ConstructEntry> _entries = new List<ConstructEntry>();

        internal override string FileName => "TsGeneratedConstruct.cs";

        internal override string TabLabel => "Constructs";
        internal override string TabDescription =>
            "Register TsBehaviours that are always active in the scene, not pooled. TsConstruct() is called once on each at startup. Example: add your HudManager here and it is initialized automatically when the world loads.";
        internal override void DrawTab(SerializedObject so) => ObjectListGUI.DrawObjectList(so, "Constructs");

        internal override void LoadConfig()
        {
            var sceneConfig = UnityEngine.Object.FindObjectOfType<TsConfig>(true);
            if (sceneConfig == null) { _entries = new List<ConstructEntry>(); return; }
            var so = new SerializedObject(sceneConfig);
            var prop = so.FindProperty("Constructs");
            var constructs = new TsBehaviour[prop.arraySize];
            for (int i = 0; i < prop.arraySize; i++)
                constructs[i] = prop.GetArrayElementAtIndex(i).objectReferenceValue as TsBehaviour;
            _entries = Resolve(constructs);
        }

        internal override string GenerateCode()
        {
            if (_entries.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UnityEngine" };
            foreach (var entry in _entries)
                if (!string.IsNullOrEmpty(entry.Namespace) && !usings.Contains(entry.Namespace))
                    usings.Add(entry.Namespace);

            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                foreach (var entry in _entries.OrderBy(e => e.Name))
                    w.Line($"[HideInInspector] [SerializeField] private {entry.TypeName} {FieldName(entry.Name)};");

                using (w.Method("public void _TsConstructStart()"))
                {
                    foreach (var entry in _entries.OrderBy(e => e.Name))
                        w.Line($"{FieldName(entry.Name)}.TsConstruct(this);");
                }
            }

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            using (w.Method("public void _TsConstructStart()"))
            { }
            return w.ToString();
        }

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            var so = new SerializedObject(root);
            foreach (var entry in _entries)
            {
                if (entry.SourceObject == null)
                {
                    Debug.LogWarning($"[ConstructModule] Construct '{entry.Name}' source object is null. Remove the missing entry from TsConfig.");
                    continue;
                }

                var prop = so.FindProperty(FieldName(entry.Name));
                if (prop == null)
                {
                    Debug.LogWarning($"[ConstructModule] Field '{FieldName(entry.Name)}' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
                    continue;
                }

                prop.objectReferenceValue = entry.SourceObject;
            }

            ApplyAndMarkDirty(so, root);
        }

        private static List<ConstructEntry> Resolve(TsBehaviour[] constructs)
        {
            if (constructs == null) return new List<ConstructEntry>();

            var entries = new List<ConstructEntry>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<UnityEngine.Object>();

            foreach (var behaviour in constructs)
            {
                if (behaviour == null)
                {
                    Debug.LogWarning("[ConstructModule] Null entry in Constructs config, remove the missing-script slot.");
                    continue;
                }
                if (!seen.Add(behaviour))
                {
                    Debug.LogWarning($"[ConstructModule] Duplicate construct '{behaviour.name}' in config, remove the duplicate.");
                    continue;
                }

                var type = behaviour.GetType();
                string goName = behaviour.gameObject.name;
                string baseName = AliasName(goName) ?? type.Name;
                string name = Deduplicate(baseName, usedNames);
                usedNames.Add(name);

                entries.Add(new ConstructEntry
                {
                    Name = name,
                    TypeName = type.Name,
                    Namespace = type.Namespace ?? string.Empty,
                    SourceObject = behaviour,
                });
            }

            return entries;
        }

        private static string FieldName(string name) => $"_construct{name}";

        private struct ConstructEntry
        {
            public string Name;
            public string TypeName;
            public string Namespace;
            public UnityEngine.Object SourceObject;
        }
    }
}
#endif
