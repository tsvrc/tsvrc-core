#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    internal class ConstructModule : TsvrcModule
    {
        private List<ConstructEntry> _entries = new List<ConstructEntry>();

        internal override string FileName => "TsvrcGeneratedConstruct.cs";

        internal override IEnumerable<string> ExposedFieldNames() => _entries.Select(e => e.Name);

        internal override void ExcludeFieldNames(IEnumerable<string> names)
        {
            var excluded = new HashSet<string>(names, StringComparer.Ordinal);
            _entries.RemoveAll(e =>
            {
                if (!excluded.Contains(e.Name)) return false;
                Debug.LogError($"[ConstructModule] Field name '{e.Name}' conflicts with another module. Use __Alias__ syntax on the GameObject to assign a unique name.");
                return true;
            });
        }

        internal override void LoadConfig()
        {
            var sceneConfig = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            _entries = Resolve(sceneConfig?.Constructs);
        }

        internal override string GenerateCode()
        {
            if (_entries.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UnityEngine" };
            foreach (var entry in _entries)
                if (!string.IsNullOrEmpty(entry.Namespace) && !usings.Contains(entry.Namespace))
                    usings.Add(entry.Namespace);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                foreach (var entry in _entries.OrderBy(e => e.Name))
                    w.Line($"[HideInInspector] [SerializeField] public {entry.TypeName} {entry.Name};");
            }

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            { }
            return w.ToString();
        }

        internal override void Wire()
        {
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType == null) return;

            var root = (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
            if (root == null) return;

            var so = new SerializedObject(root);
            foreach (var entry in _entries)
            {
                if (entry.SourceObject == null)
                {
                    Debug.LogWarning($"[ConstructModule] Construct '{entry.Name}' source object is null. Remove the missing entry from TsvrcConfig.");
                    continue;
                }

                var prop = so.FindProperty(entry.Name);
                if (prop == null)
                {
                    Debug.LogWarning($"[ConstructModule] Field '{entry.Name}' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
                    continue;
                }

                prop.objectReferenceValue = entry.SourceObject;
            }

            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }

        private static List<ConstructEntry> Resolve(TsvrcBehaviour[] constructs)
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

        // __Foo__ on the GameObject overrides the generated field name to Foo.
        private static string AliasName(string goName)
        {
            if (goName != null && goName.StartsWith("__") && goName.EndsWith("__") && goName.Length > 4)
                return goName.Substring(2, goName.Length - 4);
            return null;
        }

        private static string Deduplicate(string baseName, HashSet<string> usedNames)
        {
            string name = baseName;
            int suffix = 2;
            while (usedNames.Contains(name))
                name = $"{baseName}{suffix++}";
            return name;
        }

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
