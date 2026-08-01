#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates a _construct{Name} field per TsvrcBehaviour in TsConfig.Constructs;
    // _TsConstructStart() calls TsConstruct(this) on each in field-name order.
    internal class ConstructModule : TsModule
    {
        private const string SnapshotKey = "ConstructModule";

        private List<ConstructEntry> _entries = new List<ConstructEntry>();

        internal override string FileName => "TsGeneratedConstruct.cs";

        internal override string TabLabel => "Constructs";
        internal override string TabDescription =>
            "Register TsvrcBehaviours that are always active in the scene, not pooled. TsConstruct() is called once on each at startup. Example: add your HudManager here and it is initialized automatically when the world loads.";
        internal override void DrawTab(SerializedObject so) => ObjectListGUI.DrawObjectList(so, "Constructs",
            "No constructs registered yet. Add a TsvrcBehaviour here to have TsConstruct(this) called on it at startup.",
            warnDuplicates: true);

        internal override void LoadConfig()
        {
            var sceneConfig = TsLinkedScene.Find<TsConfig>();

            // Deliberately does NOT early-return an empty result when sceneConfig is null: that
            // would bypass ApplySnapshotFallback below, meaning a compile-broken pass on a scene
            // that hasn't loaded TsConfig yet (or ever) would collapse a real snapshot to empty
            // when it should fall back to it, same as SingletonModule's handling of the same case.
            //
            // Read as plain Object, not `as TsvrcBehaviour`: that cast silently drops any entry
            // whose script currently has no compiled type (a "Missing (Mono Script)" component,
            // typically because Assembly-CSharp is broken precisely because it's missing a field
            // this module is responsible for generating), before Resolve() ever gets a chance to
            // fall back to ScriptIndex for it. See TryResolveObjectType.
            var constructs = Array.Empty<UnityEngine.Object>();
            if (sceneConfig != null)
            {
                var so = new SerializedObject(sceneConfig);
                var prop = so.FindProperty("Constructs");
                constructs = new UnityEngine.Object[prop.arraySize];
                for (int i = 0; i < prop.arraySize; i++)
                    constructs[i] = prop.GetArrayElementAtIndex(i).objectReferenceValue;
            }

            var resolved = Resolve(constructs);
            _entries = ApplySnapshotFallback(SnapshotKey, resolved,
                e => new ModuleEntrySnapshot.Entry { Name = e.Name, TypeName = e.TypeName, Namespace = e.Namespace },
                s => new ConstructEntry { Name = s.Name, TypeName = s.TypeName, Namespace = s.Namespace, SourceObject = null });
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

        private static List<ConstructEntry> Resolve(UnityEngine.Object[] constructs)
        {
            if (constructs == null) return new List<ConstructEntry>();

            var entries = new List<ConstructEntry>();
            var usedNames = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<UnityEngine.Object>();

            foreach (var obj in constructs)
            {
                if (!TryAcceptEntry(obj, "ConstructModule", "Constructs config", "construct", seen)) continue;

                if (!(obj is Component component))
                {
                    Debug.LogWarning($"[ConstructModule] '{obj.name}' is not a component. Constructs must be TsvrcBehaviours on a scene object.");
                    continue;
                }

                if (!TryResolveObjectType(obj, out string typeName, out string ns))
                {
                    Debug.LogWarning($"[ConstructModule] Could not resolve a type for '{obj.name}'; its script may be missing. Skipping.");
                    continue;
                }

                if (!IsTsvrcBehaviourType(typeName, ns))
                {
                    Debug.LogWarning($"[ConstructModule] '{obj.name}' ({typeName}) is not a TsvrcBehaviour. Skipping.");
                    continue;
                }

                string goName = component.gameObject.name;
                string baseName = AliasName(goName) ?? typeName;
                string name = Deduplicate(baseName, usedNames);
                usedNames.Add(name);

                entries.Add(new ConstructEntry
                {
                    Name = name,
                    TypeName = typeName,
                    Namespace = ns,
                    SourceObject = obj,
                });
            }

            return entries;
        }

        private static string FieldName(string name) => $"_construct{name}";

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
