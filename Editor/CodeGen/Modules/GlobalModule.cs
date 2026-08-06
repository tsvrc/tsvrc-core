#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Globals are scene objects, so they come from TsConfig, a scene component
    // ScaffoldModule automatically creates and heals under TsGenerated, since an asset can't
    // hold a reference to a scene object. Builtin globals are asset type objects, so those
    // come from TsBuiltinConfig instead.
    internal class GlobalModule : TsModule
    {
        private const string SnapshotKey = "GlobalModule";

        private List<GlobalEntry> _entries = new List<GlobalEntry>();
        private List<string> _lastExcluded = new List<string>();
        private List<string> _lastGraceIncluded = new List<string>();

        internal override IEnumerable<string> LastTreeShakingExclusions => _lastExcluded;
        internal override IEnumerable<string> LastTreeShakingGraceIncluded => _lastGraceIncluded;

        // Tab-only UI state (tree expand/select/search), never written to TsConfig - see
        // TsGroupTreeGUI.State's own doc comment.
        private readonly TsGroupTreeGUI.State _treeState = new TsGroupTreeGUI.State();

        internal override string FileName => "TsGeneratedGlobal.cs";

        internal override string TabLabel => "Globals";
        internal override string TabDescription =>
            "Register any scene object or component as a named field on _ts. After compiling, access it from any TsvrcBehaviour via _ts.FieldName. Example: drag your GameManager here, then use _ts.GameManager from any behaviour. Groups are purely organizational - they don't affect field names.";
        internal override void DrawTab(SerializedObject so) => TsGroupTreeGUI.Draw(so, "GlobalGroups", "GlobalEntries", _treeState,
            "No globals registered yet. Add a scene object here to expose it as a field on TsGenerated.",
            warnDuplicates: true);

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override IEnumerable<string> ExposedFieldNames() => _entries.Select(e => e.Name);

        internal override void ExcludeFieldNames(IEnumerable<string> names)
        {
            var excluded = new HashSet<string>(names, StringComparer.Ordinal);
            _entries.RemoveAll(e =>
            {
                if (!excluded.Contains(e.Name)) return false;
                Debug.LogError($"[GlobalModule] Field name '{e.Name}' conflicts with another module. Use __Alias__ syntax on the GameObject to assign a unique name.");
                return true;
            });
        }

        internal override void LoadConfig()
        {
            var sceneConfig = TsLinkedScene.Find<TsConfig>();
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>(BuiltinConfigPath);

            if (sceneConfig != null)
                BreakGroupCycles("GlobalModule", sceneConfig.GlobalGroups);
            if (builtinConfig != null)
                BreakGroupCycles("GlobalModule", builtinConfig.GlobalGroups);

            var sceneGlobals = (sceneConfig?.GlobalEntries ?? Array.Empty<TsGroupedEntry>()).Select(e => e.Value);
            var builtinGlobals = (builtinConfig?.GlobalEntries ?? Array.Empty<TsGroupedEntry>()).Select(e => e.Value);
            var combined = sceneGlobals.Concat(builtinGlobals);

            var resolved = Resolve(combined);
            // Builtin-sourced entries flow through the same filter as scene-sourced ones, since
            // combined above already merged them before Resolve() ran.
            resolved = ApplyTreeShaking(sceneConfig, "GlobalModule", resolved,
                e => e.Name, TsUsageScanner.IsMemberReferenced, out int excluded, out _lastExcluded, out _lastGraceIncluded,
                TsGenerator.CurrentPassCountsForGracePeriod);
            _entries = ApplySnapshotFallback(SnapshotKey, resolved,
                e => new ModuleEntrySnapshot.Entry { Name = e.Name, TypeName = e.TypeName, Namespace = e.Namespace },
                s => new GlobalEntry { Name = s.Name, TypeName = s.TypeName, Namespace = s.Namespace, SourceObject = null },
                excluded);
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
                {
                    w.Summary("Tsvrc global.");
                    w.Line($"[HideInInspector] [SerializeField] public {entry.TypeName} {entry.Name};");
                }

                using (w.Method("public void _TsGlobalStart()"))
                {
                    foreach (var entry in _entries.OrderBy(e => e.Name))
                    {
                        if (!IsTsvrcBehaviourType(entry.TypeName, entry.Namespace)) continue;
                        w.Line($"{entry.Name}.TsConstruct(this);");
                    }
                }
            }

            return w.ToString();
        }

        private static string BuildStub() => BuildStub(null, "public void _TsGlobalStart()");

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            var so = new SerializedObject(root);
            foreach (var entry in _entries)
            {
                if (!TryFindField(so, entry.Name, "GlobalModule", out var prop)) continue;
                prop.objectReferenceValue = entry.SourceObject;
            }

            ApplyAndMarkDirty(so, root);
        }

        private static List<GlobalEntry> Resolve(IEnumerable<UnityEngine.Object> objects)
        {
            var entries = new List<GlobalEntry>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<UnityEngine.Object>();

            foreach (var obj in objects)
            {
                if (!TryAcceptEntry(obj, "GlobalModule", "config", "entry", seen)) continue;

                if (!TryResolveObjectType(obj, out string typeName, out string ns))
                {
                    Debug.LogWarning($"[GlobalModule] Could not resolve a type for '{obj.name}'; its script may be missing. Skipping.");
                    continue;
                }

                string goName = obj is Component c ? c.gameObject.name : (obj is GameObject go ? go.name : string.Empty);
                string baseName = DeriveName(typeName, goName);
                string name = Deduplicate(baseName, usedNames);
                usedNames.Add(name);

                entries.Add(new GlobalEntry
                {
                    Name = name,
                    TypeName = typeName,
                    Namespace = ns,
                    SourceObject = obj,
                });
            }

            return entries;
        }

        private static string DeriveName(string typeName, string goName)
        {
            if (typeName == "Animator")
                return (AliasName(goName) ?? goName) + "Animator";
            return AliasName(goName) ?? typeName;
        }

        private struct GlobalEntry
        {
            public string Name;
            public string TypeName;
            public string Namespace;
            public UnityEngine.Object SourceObject;
        }
    }
}
#endif
