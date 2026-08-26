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

        private List<ResolvedEntry> _entries = new List<ResolvedEntry>();
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
            "Register any scene object or component as a named field on _ts. After compiling, access it from any TsvrcBehaviour via _ts.FieldName. Example: drag your GameManager here, then use _ts.GameManager from any behaviour. Groups are organizational by default; toggle 'Namespace with group name' on a group to prefix its entries' member names (e.g. _ts.EnemiesSpawner).";
        internal override bool DrawTab(SerializedObject so) => TsGroupTreeGUI.Draw(so, "GlobalGroups", "GlobalEntries", _treeState,
            "No globals registered yet. Add a scene object here to expose it as a field on TsGenerated.",
            warnDuplicates: true, groupNaming: true, memberPrefix: "_ts.");

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override IEnumerable<string> ExposedFieldNames() => _entries.Select(e => e.Name);

        internal override void ExcludeFieldNames(IEnumerable<string> names)
        {
            var excluded = new HashSet<string>(names, StringComparer.Ordinal);
            _entries.RemoveAll(e =>
            {
                if (!excluded.Contains(e.Name)) return false;
                Debug.LogWarning($"[GlobalModule] '{e.Name}' is also provided by a higher-precedence registration, so this Global entry is dropped and '_ts.{e.Name}' comes from that instead. If this object is also registered as a Construct, remove it from Globals - the Construct already exposes it. Otherwise set a distinct Name on this entry in Tsvrc > Configure.");
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

            // Scene entries may opt into group-path namespacing; builtin entries never do (empty
            // prefix). One Resolve call covers both so deduplication spans the merged set, scene
            // entries first so they keep the unsuffixed name on a clash.
            var sceneGroups = ToGroupLookup(sceneConfig?.GlobalGroups);
            var input = new List<(UnityEngine.Object, string, string)>();
            foreach (var e in sceneConfig?.GlobalEntries ?? Array.Empty<TsGroupedEntry>())
                input.Add((e.Value, BuildGroupPrefix(e.GroupId, sceneGroups, Sanitize, respectToggle: true), e.Name));
            foreach (var e in builtinConfig?.GlobalEntries ?? Array.Empty<TsGroupedEntry>())
                input.Add((e.Value, string.Empty, e.Name));

            var resolved = Resolve(input);
            resolved = ApplyTreeShaking(sceneConfig, "GlobalModule", resolved,
                e => e.Name, TsUsageScanner.IsMemberReferenced, out int excluded, out _lastExcluded, out _lastGraceIncluded,
                TsGenerator.CurrentPassCountsForGracePeriod);
            _entries = ApplySnapshotFallback(SnapshotKey, resolved,
                e => new ModuleEntrySnapshot.Entry { Name = e.Name, TypeName = e.TypeName, Namespace = e.Namespace },
                s => new ResolvedEntry { Name = s.Name, TypeName = s.TypeName, Namespace = s.Namespace, SourceObject = null },
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

        // Globals accept any scene object or component and are named by DeriveName.
        private static readonly EntryPolicy Policy = new EntryPolicy
        {
            ModuleTag = "GlobalModule",
            ConfigLabel = "config",
            EntryNoun = "entry",
            RequireComponent = false,
            RequireTsvrcBehaviour = false,
            PrimaryName = DeriveName,
        };

        private static List<ResolvedEntry> Resolve(IEnumerable<(UnityEngine.Object value, string prefix, string explicitName)> inputs)
            => ResolveEntries(inputs, Policy);

        // The default name when an entry has no explicit one.
        private static string DeriveName(string typeName, string goName)
        {
            // A plain GameObject resolves to the literal type name "GameObject", which is useless and
            // collides across entries, so name it after the object instead. Fall back to the type
            // name only when the object's name sanitizes to nothing.
            if (typeName == "GameObject")
            {
                string fromGoName = Sanitize(goName);
                return fromGoName.Length > 0 ? fromGoName : typeName;
            }

            if (typeName == "Animator")
                return Sanitize(goName) + "Animator";

            return typeName;
        }
    }
}
#endif
