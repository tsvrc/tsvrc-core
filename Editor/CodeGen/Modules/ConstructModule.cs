#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // One private _construct{Name} field per TsvrcBehaviour in TsConfig.ConstructEntries.
    // _TsConstructStart() calls TsConstruct(this) on each, in field-name order. No public accessor -
    // use a Global entry when the behaviour also needs to be reachable as _ts.Name.
    internal class ConstructModule : TsModule
    {
        private const string SnapshotKey = "ConstructModule";

        private List<ResolvedEntry> _entries = new List<ResolvedEntry>();

        // Tab-only UI state (tree expand/select/search), never written to TsConfig - see
        // TsGroupTreeGUI.State's own doc comment.
        private readonly TsGroupTreeGUI.State _treeState = new TsGroupTreeGUI.State();

        internal override string FileName => "TsGeneratedConstruct.cs";

        internal override string TabLabel => "Constructs";
        internal override string TabDescription =>
            "Register TsvrcBehaviours that are always active in the scene, not pooled, to initialize them at startup (TsConstruct). The reference stays private, never reachable as _ts.Name, so there's no member name to configure.";
        internal override bool DrawTab(SerializedObject so) => TsGroupTreeGUI.Draw(so, "ConstructGroups", "ConstructEntries", _treeState,
            "No constructs registered yet. Add a TsvrcBehaviour here to initialize it at startup without exposing it on _ts.",
            warnDuplicates: true, groupNaming: false, memberPrefix: null);

        internal override void LoadConfig()
        {
            var sceneConfig = TsLinkedScene.Find<TsConfig>();

            // Does not early-return when sceneConfig is null: that path still runs
            // ApplySnapshotFallback, so a compile-broken pass falls back to the snapshot instead of
            // collapsing it to empty. Entries reach Resolve as UnityEngine.Object, so a
            // missing-script component still resolves via ScriptIndex.
            var input = new List<(UnityEngine.Object, string, string)>();
            if (sceneConfig != null)
            {
                BreakGroupCycles("ConstructModule", sceneConfig.ConstructGroups);
                // Field name is never user-facing, so just derive it from the type and dedup.
                foreach (var e in sceneConfig.ConstructEntries ?? Array.Empty<TsGroupedEntry>())
                    input.Add((e.Value, string.Empty, string.Empty));
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
                    w.Line($"[HideInInspector] [SerializeField] private {entry.TypeName} {FieldName(entry.Name)};");

                using (w.Method("public void _TsConstructStart()"))
                {
                    foreach (var entry in _entries.OrderBy(e => e.Name))
                        w.Line($"{FieldName(entry.Name)}.TsConstruct(this);");
                }
            }

            return w.ToString();
        }

        private static string BuildStub() => BuildStub(null, "public void _TsConstructStart()");

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

        // A construct must be a TsvrcBehaviour on a scene object; its private field is always named
        // after its type. The shared ResolveEntries loop does the accept/resolve/validate/name/dedup.
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

        // A construct's field name is always its component type name, already a valid identifier;
        // duplicates of the same type are disambiguated by ResolveEntries' own suffix dedup.
        private static string ConstructPrimaryName(string typeName, string goName) => typeName;

        private static string FieldName(string name) => $"_construct{name}";
    }
}
#endif
