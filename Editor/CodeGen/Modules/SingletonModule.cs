#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Singletons are scene objects, so they come from TsConfig, a scene component
    // ScaffoldModule automatically creates and heals under TsGenerated, since an asset can't
    // hold a reference to a scene object. Builtin singletons are asset type objects, so those
    // come from TsBuiltinConfig instead.
    internal class SingletonModule : TsModule
    {
        private const string SnapshotKey = "SingletonModule";

        private List<SingletonEntry> _entries = new List<SingletonEntry>();
        private List<string> _lastExcluded = new List<string>();
        private List<string> _lastGraceIncluded = new List<string>();

        internal override IEnumerable<string> LastTreeShakingExclusions => _lastExcluded;
        internal override IEnumerable<string> LastTreeShakingGraceIncluded => _lastGraceIncluded;

        internal override string FileName => "TsGeneratedSingleton.cs";

        internal override string TabLabel => "Singletons";
        internal override string TabDescription =>
            "Register any scene object or component as a named field on _ts. After compiling, access it from any TsvrcBehaviour via _ts.FieldName. Example: drag your GameManager here, then use _ts.GameManager from any behaviour.";
        internal override void DrawTab(SerializedObject so) => ObjectListGUI.DrawObjectList(so, "Singletons",
            "No singletons registered yet. Add a scene object here to expose it as a field on TsGenerated.",
            warnDuplicates: true);

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override IEnumerable<string> ExposedFieldNames() => _entries.Select(e => e.Name);

        internal override void ExcludeFieldNames(IEnumerable<string> names)
        {
            var excluded = new HashSet<string>(names, StringComparer.Ordinal);
            _entries.RemoveAll(e =>
            {
                if (!excluded.Contains(e.Name)) return false;
                Debug.LogError($"[SingletonModule] Field name '{e.Name}' conflicts with another module. Use __Alias__ syntax on the GameObject to assign a unique name.");
                return true;
            });
        }

        internal override void LoadConfig()
        {
            var sceneConfig = TsLinkedScene.Find<TsConfig>();
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>(BuiltinConfigPath);

            var sceneSingletons = Array.Empty<UnityEngine.Object>();
            if (sceneConfig != null)
            {
                var so = new SerializedObject(sceneConfig);
                var prop = so.FindProperty("Singletons");
                sceneSingletons = new UnityEngine.Object[prop.arraySize];
                for (int i = 0; i < prop.arraySize; i++)
                    sceneSingletons[i] = prop.GetArrayElementAtIndex(i).objectReferenceValue;
            }

            var combined = sceneSingletons
                .Concat(builtinConfig?.Singletons ?? Array.Empty<UnityEngine.Object>());

            var resolved = Resolve(combined);
            // Builtin-sourced entries flow through the same filter as scene-sourced ones, since
            // combined above already merged them before Resolve() ran.
            resolved = ApplyTreeShaking(sceneConfig, "SingletonModule", resolved,
                e => e.Name, TsUsageScanner.IsMemberReferenced, out int excluded, out _lastExcluded, out _lastGraceIncluded);
            _entries = ApplySnapshotFallback(SnapshotKey, resolved,
                e => new ModuleEntrySnapshot.Entry { Name = e.Name, TypeName = e.TypeName, Namespace = e.Namespace },
                s => new SingletonEntry { Name = s.Name, TypeName = s.TypeName, Namespace = s.Namespace, SourceObject = null },
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
                    w.Summary("Tsvrc singleton.");
                    w.Line($"[HideInInspector] [SerializeField] public {entry.TypeName} {entry.Name};");
                }

                using (w.Method("public void _TsSingletonStart()"))
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

        private static string BuildStub() => BuildStub(null, "public void _TsSingletonStart()");

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            var so = new SerializedObject(root);
            foreach (var entry in _entries)
            {
                if (!TryFindField(so, entry.Name, "SingletonModule", out var prop)) continue;
                prop.objectReferenceValue = entry.SourceObject;
            }

            ApplyAndMarkDirty(so, root);
        }

        private static List<SingletonEntry> Resolve(IEnumerable<UnityEngine.Object> objects)
        {
            var entries = new List<SingletonEntry>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<UnityEngine.Object>();

            foreach (var obj in objects)
            {
                if (!TryAcceptEntry(obj, "SingletonModule", "config", "entry", seen)) continue;

                if (!TryResolveObjectType(obj, out string typeName, out string ns))
                {
                    Debug.LogWarning($"[SingletonModule] Could not resolve a type for '{obj.name}'; its script may be missing. Skipping.");
                    continue;
                }

                string goName = obj is Component c ? c.gameObject.name : (obj is GameObject go ? go.name : string.Empty);
                string baseName = DeriveName(typeName, goName);
                string name = Deduplicate(baseName, usedNames);
                usedNames.Add(name);

                entries.Add(new SingletonEntry
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

        private struct SingletonEntry
        {
            public string Name;
            public string TypeName;
            public string Namespace;
            public UnityEngine.Object SourceObject;
        }
    }
}
#endif
