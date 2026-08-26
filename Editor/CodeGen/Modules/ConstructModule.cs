#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates a _construct{Name} field per TsvrcBehaviour in TsConfig.ConstructEntries;
    // _TsConstructStart() calls TsConstruct(this) on each in field-name order.
    internal class ConstructModule : TsModule
    {
        private const string SnapshotKey = "ConstructModule";

        private List<ResolvedEntry> _entries = new List<ResolvedEntry>();

        // Names whose _ts.Name accessor is suppressed this pass because another module of equal or
        // higher precedence owns the name. The private field and its TsConstruct call still emit.
        private HashSet<string> _suppressedAccessors = new HashSet<string>(StringComparer.Ordinal);

        // Tab-only UI state (tree expand/select/search), never written to TsConfig - see
        // TsGroupTreeGUI.State's own doc comment.
        private readonly TsGroupTreeGUI.State _treeState = new TsGroupTreeGUI.State();

        internal override string FileName => "TsGeneratedConstruct.cs";

        internal override string TabLabel => "Constructs";
        internal override string TabDescription =>
            "Register TsvrcBehaviours that are always active in the scene, not pooled. Each is initialized once at startup (TsConstruct) AND reachable as _ts.Name - one registration, both behaviours, so you don't also need a separate Global entry. Example: add your HudManager here and use _ts.HudManager anywhere. Groups are organizational by default; toggle 'Namespace with group name' on a group to prefix member names.";
        internal override bool DrawTab(SerializedObject so) => TsGroupTreeGUI.Draw(so, "ConstructGroups", "ConstructEntries", _treeState,
            "No constructs registered yet. Add a TsvrcBehaviour here to initialize it at startup and expose it as _ts.Name.",
            warnDuplicates: true, groupNaming: true, memberPrefix: "_ts.");

        internal override void LoadConfig()
        {
            _suppressedAccessors = new HashSet<string>(StringComparer.Ordinal);
            var sceneConfig = TsLinkedScene.Find<TsConfig>();

            // Does not early-return when sceneConfig is null: that path still runs
            // ApplySnapshotFallback, so a compile-broken pass falls back to the snapshot instead of
            // collapsing it to empty. Entries reach Resolve as UnityEngine.Object, so a
            // missing-script component still resolves via ScriptIndex.
            var input = new List<(UnityEngine.Object, string, string)>();
            if (sceneConfig != null)
            {
                BreakGroupCycles("ConstructModule", sceneConfig.ConstructGroups);
                var groups = ToGroupLookup(sceneConfig.ConstructGroups);
                foreach (var e in sceneConfig.ConstructEntries ?? Array.Empty<TsGroupedEntry>())
                    input.Add((e.Value, BuildGroupPrefix(e.GroupId, groups, Sanitize, respectToggle: true), e.Name));
            }

            var resolved = Resolve(input);
            _entries = ApplySnapshotFallback(SnapshotKey, resolved,
                e => new ModuleEntrySnapshot.Entry { Name = e.Name, TypeName = e.TypeName, Namespace = e.Namespace },
                s => new ResolvedEntry { Name = s.Name, TypeName = s.TypeName, Namespace = s.Namespace, SourceObject = null });
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
                    w.Line($"[HideInInspector] [SerializeField] private {entry.TypeName} {FieldName(entry.Name)};");
                    // A construct is initialized at startup and exposed as _ts.Name. The accessor is
                    // omitted only when a higher-precedence module owns the name; the init still runs.
                    if (!_suppressedAccessors.Contains(entry.Name))
                        w.Line($"public {entry.TypeName} {entry.Name} => {FieldName(entry.Name)};");
                }

                using (w.Method("public void _TsConstructStart()"))
                {
                    foreach (var entry in _entries.OrderBy(e => e.Name))
                        w.Line($"{FieldName(entry.Name)}.TsConstruct(this);");
                }
            }

            return w.ToString();
        }

        private static string BuildStub() => BuildStub(null, "public void _TsConstructStart()");

        // A construct exposes _ts.Name, so it participates in cross-module collision detection.
        internal override IEnumerable<string> ExposedFieldNames() => _entries.Select(e => e.Name);

        // A construct's accessor supersedes a Global field of the same name, so registering the same
        // object as both resolves to the construct. A tie with another equal-precedence module
        // suppresses the accessor instead.
        internal override int FieldNamePrecedence => 100;

        internal override void ExcludeFieldNames(IEnumerable<string> names)
        {
            var mine = new HashSet<string>(_entries.Select(e => e.Name), StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!mine.Contains(name) || !_suppressedAccessors.Add(name)) continue;
                Debug.LogWarning($"[ConstructModule] '_ts.{name}' collides with another same-precedence registration; " +
                    "keeping this construct's startup initialization but not its accessor. Set a distinct Name on one in Tsvrc > Configure to expose both.");
            }
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

                if (!TryFindField(so, FieldName(entry.Name), "ConstructModule", out var prop)) continue;

                prop.objectReferenceValue = entry.SourceObject;
            }

            ApplyAndMarkDirty(so, root);
        }

        // A construct must be a TsvrcBehaviour on a scene object, named by its explicit Name or its
        // type name. The shared ResolveEntries loop does the accept/resolve/validate/name/dedup.
        private static readonly EntryPolicy Policy = new EntryPolicy
        {
            ModuleTag = "ConstructModule",
            ConfigLabel = "Constructs config",
            EntryNoun = "construct",
            RequireComponent = true,
            RequireTsvrcBehaviour = true,
            PrimaryName = ConstructPrimaryName,
        };

        private static List<ResolvedEntry> Resolve(IEnumerable<(UnityEngine.Object value, string prefix, string explicitName)> inputs)
            => ResolveEntries(inputs, Policy);

        // The default name when a construct has no explicit one: its component type name, already a
        // valid identifier.
        private static string ConstructPrimaryName(string typeName, string goName) => typeName;

        private static string FieldName(string name) => $"_construct{name}";
    }
}
#endif
