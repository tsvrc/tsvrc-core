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
    // Instantiates each prefab under a "Factories" child (inactive by default) at wire
    // time. Builtin and user factory groups are merged; group names become a name prefix.
    internal class FactoryModule : TsModule
    {
        private const string SnapshotKey = "FactoryModule";

        private List<FactoryEntry> _entries = new List<FactoryEntry>();
        private List<string> _lastExcluded = new List<string>();
        private List<string> _lastGraceIncluded = new List<string>();

        internal override IEnumerable<string> LastTreeShakingExclusions => _lastExcluded;
        internal override IEnumerable<string> LastTreeShakingGraceIncluded => _lastGraceIncluded;

        // Tab-only UI state (tree expand/select/search), never written to TsConfig - see
        // TsGroupTreeGUI.State's own doc comment.
        private readonly TsGroupTreeGUI.State _treeState = new TsGroupTreeGUI.State();

        internal override string FileName => "TsGeneratedFactory.cs";

        internal override string TabLabel => "Factories";
        internal override string TabDescription =>
            "Register prefabs organized into nested groups. Generates a Create{Group}{SubGroup}...{Name}(Transform parent) method for each entry, prefixed by its full group ancestor chain. WARNING: instantiated objects do not receive a VRChat network ID and cannot send or receive network events. Use Pool for networked objects.";

        internal override bool DrawTab(SerializedObject so) => TsGroupTreeGUI.Draw(so, "FactoryGroups", "FactoryEntries", _treeState,
            "No factory prefabs registered yet. Add a group on the left, then add prefabs inside it for on-demand instantiation.",
            assetsOnly: true, memberPrefix: "Create", memberSuffix: "(parent)", prefixRespectsToggle: false);

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override void LoadConfig()
        {
            var userConfig = TsLinkedScene.Find<TsConfig>();
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>(BuiltinConfigPath);

            if (userConfig != null)
                BreakGroupCycles("FactoryModule", userConfig.FactoryGroups);
            if (builtinConfig != null)
                BreakGroupCycles("FactoryModule", builtinConfig.FactoryGroups);

            var resolved = BuildEntries(userConfig, builtinConfig);
            // A Factory entry's usage signature is its generated Create{Name}(...) call site, not
            // a bare member access. Builtin-sourced groups flow through the same filter as user
            // groups, since BuildEntries already merged them.
            resolved = ApplyTreeShaking(userConfig, "FactoryModule", resolved,
                e => e.Name, name => TsUsageScanner.IsMethodCallReferenced($"Create{name}"),
                out int excluded, out _lastExcluded, out _lastGraceIncluded,
                TsGenerator.CurrentPassCountsForGracePeriod);
            _entries = ApplySnapshotFallback(SnapshotKey, resolved,
                e => new ModuleEntrySnapshot.Entry { Name = e.Name, TypeName = e.TypeName, Namespace = e.TypeNamespace },
                s => new FactoryEntry
                {
                    Name = s.Name,
                    TypeName = s.TypeName,
                    TypeNamespace = s.Namespace,
                    IsTsvrcBehaviour = IsTsvrcBehaviourType(s.TypeName, s.Namespace),
                    PrefabAsset = null,
                },
                excluded);
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

            // Builtin entries resolved first, so a name collision between a builtin and a
            // world-config prefab always suffixes the world-config one, never the builtin.
            AddEntries(builtinConfig?.FactoryEntries, builtinConfig?.FactoryGroups, usedNames, entries);
            AddEntries(config?.FactoryEntries, config?.FactoryGroups, usedNames, entries);

            return entries;
        }

        // One config source's (scene or builtin) grouped entries resolved into FactoryEntry,
        // each entry's method-name prefix computed by walking its group's full ancestor chain
        // (see TsModule.BuildGroupPrefix). usedNames/entries are threaded through both calls in
        // BuildEntries above so a later source's collisions are checked against every name
        // already assigned.
        private static void AddEntries(TsGroupedEntry[] groupedEntries, TsGroup[] groups,
            HashSet<string> usedNames, List<FactoryEntry> entries)
        {
            if (groupedEntries == null) return;
            var groupsById = ToGroupLookup(groups);

            foreach (var entry in groupedEntries)
            {
                // A deleted prefab reference (a null slot) logs, matching Global/Construct/
                // Pool's wording. Deliberately does not route through the shared TryAcceptEntry
                // helper the way Pool does: the same prefab registered twice here (once per
                // differently-named group, e.g. "Dungeon" and "Boss" both spawning the same bullet
                // prefab) is a legitimate use case, not a mistake, so duplicate-reference
                // detection would be a false positive.
                var obj = entry?.Value;
                if (obj == null)
                {
                    Debug.LogWarning("[FactoryModule] Null entry in config, remove the missing-script slot.");
                    continue;
                }

                var prefab = obj is Component c ? c.gameObject : obj as GameObject;
                if (prefab == null) continue;

                if (!EditorUtility.IsPersistent(prefab))
                {
                    Debug.LogWarning($"[FactoryModule] '{prefab.name}' is a scene object, not a prefab asset. Drag a prefab asset from the Project window instead. Skipping.");
                    continue;
                }

                string prefix = BuildGroupPrefix(entry.GroupId, groupsById, Sanitize);
                string leaf = Sanitize(entry.Name);
                if (leaf.Length == 0) leaf = Sanitize(prefab.name);
                string name = Deduplicate(prefix + leaf, usedNames);
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
