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

        internal override string FileName => "TsvrcConstructBehaviour.cs";

        internal override void LoadConfig()
        {
            var sceneConfig = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            _entries = Resolve(sceneConfig?.Constructs);
        }

        internal override string GenerateCode()
        {
            if (_entries.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UdonSharp", "UnityEngine" };
            foreach (var entry in _entries)
                if (!string.IsNullOrEmpty(entry.Namespace) && !usings.Contains(entry.Namespace))
                    usings.Add(entry.Namespace);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.ConstructClassName} : UdonSharpBehaviour"))
            {
                foreach (var entry in _entries.OrderBy(e => e.Name))
                    w.Line($"[HideInInspector] [SerializeField] public {entry.TypeName} {entry.Name};");

                using (w.Method("void Start()")) { }
            }

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.ConstructClassName} : UdonSharpBehaviour"))
            using (w.Method("void Start()"))
            { }
            return w.ToString();
        }

        internal override void Wire()
        {
            var constructType = ScaffoldModule.FindConstructType();
            if (constructType == null) return;

            var constructComp = (Component)UnityEngine.Object.FindObjectOfType(constructType, true);
            if (constructComp == null) return;

            var so = new SerializedObject(constructComp);
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
                    Debug.LogWarning($"[ConstructModule] Field '{entry.Name}' not found on {ScaffoldModule.ConstructClassName}. Force compile to regenerate.");
                    continue;
                }

                prop.objectReferenceValue = entry.SourceObject;
            }

            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(constructComp.gameObject.scene);
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
