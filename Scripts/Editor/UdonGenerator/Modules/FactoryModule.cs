#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Tsvrc.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    internal class FactoryModule : TsvrcModule
    {
        private List<FactoryEntry> _entries = new List<FactoryEntry>();

        internal override string FileName => "TsvrcFactoryBehaviour.cs";

        internal override void LoadConfig()
        {
            _entries = BuildEntries();
        }

        internal override string GenerateCode()
        {
            if (_entries.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UdonSharp", "UnityEngine" };
            foreach (var entry in _entries)
                if (!string.IsNullOrEmpty(entry.TypeNamespace) && !usings.Contains(entry.TypeNamespace))
                    usings.Add(entry.TypeNamespace);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.FactoryClassName} : UdonSharpBehaviour"))
            {
                foreach (var entry in _entries)
                    w.Line($"[SerializeField] private GameObject {FieldName(entry.Name)};");

                using (w.Method("void Start()")) { }

                foreach (var entry in _entries)
                {
                    using (w.Method($"public {entry.TypeName} Create{entry.Name}(Transform parent)"))
                    {
                        w.Line($"var go = (GameObject)Instantiate({FieldName(entry.Name)}, parent);");
                        w.Line("if (go == null) return null;");
                        w.Line("go.SetActive(true);");
                        if (entry.TypeName == "GameObject")
                            w.Line("return go;");
                        else
                            w.Line($"return go.GetComponent<{entry.TypeName}>();");
                    }
                }
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
            using (w.Block($"public class {ScaffoldModule.FactoryClassName} : UdonSharpBehaviour"))
            using (w.Method("void Start()"))
            { }
            return w.ToString();
        }

        internal override bool OnSceneHierarchyChanged()
        {
            if (_entries.Count == 0) return false;
            var factoryType = ScaffoldModule.FindFactoryType();
            if (factoryType == null) return false;
            var factoryComp = (Component)UnityEngine.Object.FindObjectOfType(factoryType, true);
            if (factoryComp == null) return false;
            return factoryComp.transform.Find("Factories") == null;
        }

        internal override void Wire()
        {
            var factoryType = ScaffoldModule.FindFactoryType();
            if (factoryType == null) return;

            var factoryComp = (Component)UnityEngine.Object.FindObjectOfType(factoryType, true);
            if (factoryComp == null) return;

            var existing = factoryComp.transform.Find("Factories");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            if (_entries.Count == 0) return;

            var so = new SerializedObject(factoryComp);
            GameObject factoriesContainer = null;

            foreach (var entry in _entries)
            {
                var prop = so.FindProperty(FieldName(entry.Name));
                if (prop == null)
                {
                    Debug.LogWarning($"[FactoryModule] Field '{FieldName(entry.Name)}' not found on {ScaffoldModule.FactoryClassName}. Force compile to regenerate.");
                    continue;
                }

                if (factoriesContainer == null)
                {
                    factoriesContainer = new GameObject("Factories");
                    Undo.RegisterCreatedObjectUndo(factoriesContainer, "Create Factories Container");
                    factoriesContainer.transform.SetParent(factoryComp.transform, false);
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.PrefabAsset, factoriesContainer.transform);
                if (instance == null)
                {
                    Debug.LogWarning($"[FactoryModule] Failed to instantiate factory prefab '{entry.Name}'. The prefab asset may be missing.");
                    continue;
                }

                instance.name = entry.Name;
                instance.SetActive(false);
                Undo.RegisterCreatedObjectUndo(instance, $"Create {entry.Name} factory instance");

                prop.objectReferenceValue = instance;
            }

            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(factoryComp.gameObject.scene);
        }

        private static List<FactoryEntry> BuildEntries()
        {
            var config = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            if (config?.Factories == null) return new List<FactoryEntry>();

            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<FactoryEntry>();

            foreach (var group in config.Factories)
            {
                if (group?.Prefabs == null) continue;
                string prefix = string.IsNullOrEmpty(group.GroupName) ? string.Empty : Sanitize(group.GroupName);

                foreach (var obj in group.Prefabs)
                {
                    if (obj == null) continue;

                    var prefab = obj is Component c ? c.gameObject : obj as GameObject;
                    if (prefab == null) continue;

                    if (!EditorUtility.IsPersistent(prefab))
                    {
                        Debug.LogWarning($"[FactoryModule] '{prefab.name}' is a scene object, not a prefab asset. Drag a prefab asset from the Project window instead. Skipping.");
                        continue;
                    }

                    string name = Deduplicate(prefix + Sanitize(prefab.name), usedNames);
                    usedNames.Add(name);

                    var behaviour = prefab.GetComponent<TsvrcBehaviour>();
                    string typeName = behaviour != null ? behaviour.GetType().Name : "GameObject";
                    string typeNamespace = behaviour != null ? (behaviour.GetType().Namespace ?? string.Empty) : string.Empty;

                    entries.Add(new FactoryEntry
                    {
                        Name = name,
                        TypeName = typeName,
                        TypeNamespace = typeNamespace,
                        PrefabAsset = prefab,
                    });
                }
            }

            return entries;
        }

        private static string FieldName(string name) => $"_factory{name}";

        private static string Deduplicate(string baseName, HashSet<string> usedNames)
        {
            string name = baseName;
            int suffix = 2;
            while (usedNames.Contains(name))
                name = $"{baseName}{suffix++}";
            return name;
        }

        // Strips __Alias__ markers, splits on non-alphanumeric separators, PascalCases each word,
        // and prepends '_' if the result starts with a digit.
        private static string Sanitize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            if (raw.StartsWith("__") && raw.EndsWith("__") && raw.Length > 4)
                raw = raw.Substring(2, raw.Length - 4);

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
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        private struct FactoryEntry
        {
            public string Name;
            public string TypeName;
            public string TypeNamespace;
            public GameObject PrefabAsset;
        }
    }
}
#endif
