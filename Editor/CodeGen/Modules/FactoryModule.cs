#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Tsvrc.Core;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Instantiates each prefab under a "Factories" child (inactive by default) at wire
    // time. Builtin and user factory groups are merged; group names become a name prefix.
    internal class FactoryModule : TsModule
    {
        private const string SnapshotKey = "FactoryModule";

        private List<FactoryEntry> _entries = new List<FactoryEntry>();

        // Tab-only UI state, keyed by array index (not group name) so the key is stable while
        // the user is typing and the foldout never collapses mid-edit.
        private readonly Dictionary<int, bool> _foldouts = new Dictionary<int, bool>();
        private static readonly GUIContent LabelGroupName = new GUIContent("Group Name");
        private static readonly GUIContent LabelPrefabs = new GUIContent("Prefabs");

        internal override string FileName => "TsGeneratedFactory.cs";

        internal override string TabLabel => "Factories";
        internal override string TabDescription =>
            "Register prefabs organized into named groups. Generates a Create{Group}{Name}(Transform parent) method for each entry. WARNING: instantiated objects do not receive a VRChat network ID and cannot send or receive network events. Use Pool for networked objects.";

        internal override void DrawTab(SerializedObject so)
        {
            var factoriesProp = so.FindProperty("Factories");

            if (factoriesProp.arraySize == 0)
                TsEditorGUI.DrawStatusBox(
                    "No factory groups registered yet. Add a group below to register prefabs for on-demand instantiation.",
                    MessageType.None);

            int toDelete = -1;
            for (int i = 0; i < factoriesProp.arraySize; i++)
            {
                var groupProp = factoriesProp.GetArrayElementAtIndex(i);
                var groupNameProp = groupProp.FindPropertyRelative("GroupName");
                var prefabsProp = groupProp.FindPropertyRelative("Prefabs");

                string groupName = groupNameProp.stringValue;
                int prefabCount = prefabsProp.arraySize;

                if (!_foldouts.TryGetValue(i, out bool expanded))
                    expanded = false;

                string foldoutLabel = string.IsNullOrEmpty(groupName)
                    ? $"(unnamed)   ({prefabCount} prefab{(prefabCount == 1 ? "" : "s")})"
                    : $"{groupName}   ({prefabCount} prefab{(prefabCount == 1 ? "" : "s")})";

                EditorGUILayout.BeginHorizontal();
                // Foldout is UI-only state. Save and restore GUI.changed around it so toggling
                // one never reads back as "the user edited TsConfig" to any future change check.
                bool prevChanged = GUI.changed;
                GUI.changed = false;
                expanded = EditorGUILayout.Foldout(expanded, foldoutLabel, true);
                _foldouts[i] = expanded;
                GUI.changed = prevChanged;
                if (ObjectListGUI.DeleteButton())
                    toDelete = i;
                EditorGUILayout.EndHorizontal();

                if (expanded)
                {
                    EditorGUI.indentLevel++;
                    // DelayedTextField, not PropertyField: the name feeds Sanitize(name) into the
                    // generated Create{Group}{Name} method, and TsConfig is a watched type, so
                    // PropertyField would write TsGeneratedFactory.cs and refresh on every keystroke.
                    string committedName = EditorGUILayout.DelayedTextField(LabelGroupName, groupNameProp.stringValue);
                    if (committedName != groupNameProp.stringValue)
                        groupNameProp.stringValue = committedName;
                    // Read groupName after the field so the prefix preview reflects the committed value.
                    string currentName = groupNameProp.stringValue;
                    string preview = string.IsNullOrWhiteSpace(currentName)
                        ? "Create…"
                        : $"Create{Sanitize(currentName)}…";
                    EditorGUILayout.LabelField($"Prefix:  {preview}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(LabelPrefabs);
                    ObjectListGUI.DrawObjectList(prefabsProp, assetsOnly: true);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(2);
            }

            // Deletion deferred outside the draw loop to avoid index invalidation.
            if (toDelete >= 0)
            {
                factoriesProp.DeleteArrayElementAtIndex(toDelete);
                ShiftFoldoutsAfterDelete(toDelete);
            }

            EditorGUILayout.Space(4);
            if (GUILayout.Button("+ Add Factory Group"))
            {
                int newIndex = factoriesProp.arraySize;
                factoriesProp.InsertArrayElementAtIndex(newIndex);
                var newGroup = factoriesProp.GetArrayElementAtIndex(newIndex);
                newGroup.FindPropertyRelative("GroupName").stringValue = string.Empty;
                newGroup.FindPropertyRelative("Prefabs").ClearArray();
                // Auto-expand the new group so the user can immediately name it.
                _foldouts[newIndex] = true;
            }
        }

        private void ShiftFoldoutsAfterDelete(int deletedIndex)
        {
            _foldouts.Remove(deletedIndex);
            // Shift all entries above the deleted index down by one.
            var keys = new List<int>(_foldouts.Keys);
            keys.Sort();
            foreach (int key in keys)
            {
                if (key > deletedIndex)
                {
                    bool val = _foldouts[key];
                    _foldouts.Remove(key);
                    _foldouts[key - 1] = val;
                }
            }
        }

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override void LoadConfig()
        {
            var userConfig = TsLinkedScene.Find<TsConfig>();
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>(BuiltinConfigPath);
            var resolved = BuildEntries(userConfig, builtinConfig);
            _entries = ApplySnapshotFallback(SnapshotKey, resolved,
                e => new ModuleEntrySnapshot.Entry { Name = e.Name, TypeName = e.TypeName, Namespace = e.TypeNamespace },
                s => new FactoryEntry
                {
                    Name = s.Name,
                    TypeName = s.TypeName,
                    TypeNamespace = s.Namespace,
                    IsTsvrcBehaviour = IsTsvrcBehaviourType(s.TypeName, s.Namespace),
                    PrefabAsset = null,
                });
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

        private static string BuildStub() => BuildStub(new[] { "UdonSharp", "UnityEngine" });

        internal override bool OnSceneHierarchyChanged()
        {
            if (_entries.Count == 0) return false;
            var root = FindRoot();
            if (root == null) return false;
            return root.transform.Find("Factories") == null;
        }

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            var existing = root.transform.Find("Factories");

            if (_entries.Count == 0)
            {
                if (existing != null)
                    Undo.DestroyObjectImmediate(existing.gameObject);
                return;
            }

            if (IsFactoriesAlreadyWired(root, existing)) return;

            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var so = new SerializedObject(root);
            GameObject factoriesContainer = null;

            foreach (var entry in _entries)
            {
                // A snapshot-restored entry (see TsModule.ApplySnapshotFallback) has no prefab
                // reference to instantiate from, and PrefabUtility.InstantiatePrefab throws on a
                // literal null target, so this must be skipped explicitly rather than falling
                // through into that call.
                if (entry.PrefabAsset == null)
                {
                    Debug.LogWarning($"[FactoryModule] Factory '{entry.Name}' prefab asset is null. Remove the missing entry from TsConfig.");
                    continue;
                }

                if (!TryFindField(so, FieldName(entry.Name), "FactoryModule", out var prop)) continue;

                var (container, instance) = CreateAndAssignInstance(prop, entry.PrefabAsset, entry.Name, factoriesContainer, root.transform);
                factoriesContainer = container;
                if (instance == null)
                    Debug.LogWarning($"[FactoryModule] Failed to instantiate factory prefab '{entry.Name}'. The prefab asset may be missing.");
            }

            ApplyAndMarkDirty(so, root);
        }

        private static List<FactoryEntry> BuildEntries(TsConfig config, TsBuiltinConfig builtinConfig)
        {
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var entries = new List<FactoryEntry>();

            var allGroups = (builtinConfig?.Factories ?? Array.Empty<TsFactoryGroup>())
                .Concat(config?.Factories ?? Array.Empty<TsFactoryGroup>());

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

        // Testable in isolation via reflection against any SerializedProperty and parent
        // Transform, not tied to the real compiled root.
        private static (GameObject container, GameObject instance) CreateAndAssignInstance(
            SerializedProperty prop, UnityEngine.Object prefabAsset, string entryName, GameObject existingContainer, Transform rootTransform)
        {
            var container = existingContainer;
            if (container == null)
            {
                container = new GameObject("Factories");
                Undo.RegisterCreatedObjectUndo(container, "Create Factories Container");
                container.transform.SetParent(rootTransform, false);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, container.transform);
            if (instance == null) return (container, null);

            instance.name = entryName;
            instance.SetActive(false);
            Undo.RegisterCreatedObjectUndo(instance, $"Create {entryName} factory instance");

            prop.objectReferenceValue = instance;
            return (container, instance);
        }

        private bool IsFactoriesAlreadyWired(Component root, Transform existing)
        {
            if (existing == null || existing.childCount != _entries.Count) return false;

            SerializedObject so = null;

            foreach (var entry in _entries)
            {
                var childTransform = existing.Find(entry.Name);
                if (childTransform == null) return false;

                if (PrefabUtility.GetCorrespondingObjectFromSource(childTransform.gameObject) != entry.PrefabAsset)
                    return false;

                if (childTransform.gameObject.activeSelf) return false;

                if (so == null) so = new SerializedObject(root);
                var prop = so.FindProperty(FieldName(entry.Name));
                if (prop == null || prop.objectReferenceValue != (UnityEngine.Object)childTransform.gameObject) return false;
            }

            return true;
        }

        private static string FieldName(string name) => $"_factory{name}";

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
