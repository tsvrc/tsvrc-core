#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
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

        internal override string FileName => "TsvrcGeneratedFactory.cs";

        private const string BuiltinConfigPath = "Assets/Tsvrc/TsvrcBuiltinConfig.asset";

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override void LoadConfig()
        {
            var userConfig = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsvrcBuiltinConfig>(BuiltinConfigPath);
            _entries = BuildEntries(userConfig, builtinConfig);
        }

        internal override string GenerateCode()
        {
            if (_entries.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UdonSharp", "UnityEngine" };
            foreach (var entry in _entries)
                if (!string.IsNullOrEmpty(entry.TypeNamespace) && !usings.Contains(entry.TypeNamespace))
                    usings.Add(entry.TypeNamespace);

            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                foreach (var entry in _entries)
                    w.Line($"[HideInInspector] [SerializeField] private GameObject {FieldName(entry.Name)};");

                foreach (var entry in _entries)
                {
                    using (w.Method($"public {entry.TypeName} Create{entry.Name}(Transform parent)"))
                    {
                        w.Line($"var go = (GameObject)Instantiate({FieldName(entry.Name)}, parent);");
                        w.Line("if (go == null) return null;");
                        w.Line("go.SetActive(true);");
                        if (entry.TypeName == "GameObject")
                        {
                            w.Line("return go;");
                        }
                        else if (entry.IsTsvrcBehaviour)
                        {
                            w.Line($"var instance = go.GetComponent<{entry.TypeName}>();");
                            w.Line("if (instance != null) instance.TsConstruct(this);");
                            w.Line("return instance;");
                        }
                        else
                        {
                            w.Line($"return go.GetComponent<{entry.TypeName}>();");
                        }
                    }
                }
            }

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            { }
            return w.ToString();
        }

        internal override bool OnSceneHierarchyChanged()
        {
            if (_entries.Count == 0) return false;
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType == null) return false;
            var root = (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
            if (root == null) return false;
            return root.transform.Find("Factories") == null;
        }

        internal override void Wire()
        {
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType == null) return;

            var root = (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
            if (root == null) return;

            var existing = root.transform.Find("Factories");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            if (_entries.Count == 0) return;

            var so = new SerializedObject(root);
            GameObject factoriesContainer = null;

            foreach (var entry in _entries)
            {
                var prop = so.FindProperty(FieldName(entry.Name));
                if (prop == null)
                {
                    Debug.LogWarning($"[FactoryModule] Field '{FieldName(entry.Name)}' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
                    continue;
                }

                if (factoriesContainer == null)
                {
                    factoriesContainer = new GameObject("Factories");
                    Undo.RegisterCreatedObjectUndo(factoriesContainer, "Create Factories Container");
                    factoriesContainer.transform.SetParent(root.transform, false);
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
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }

        private static List<FactoryEntry> BuildEntries(TsvrcConfig config, TsvrcBuiltinConfig builtinConfig)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<FactoryEntry>();

            var allGroups = (builtinConfig?.Factories ?? Array.Empty<TsvrcFactoryGroup>())
                .Concat(config?.Factories ?? Array.Empty<TsvrcFactoryGroup>());

            foreach (var group in allGroups)
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
                        IsTsvrcBehaviour = behaviour != null,
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
            public bool IsTsvrcBehaviour;
            public GameObject PrefabAsset;
        }
    }
}
#endif
