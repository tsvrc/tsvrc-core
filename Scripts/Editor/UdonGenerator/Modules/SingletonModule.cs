#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    // Generates a holder behaviour (TsvrcSingletonBehaviour) with one public field per
    // configured singleton, wired by direct reference. See SingletonModule-Design.md for the
    // full scenario analysis behind these choices.
    //
    // Singletons are scene objects by nature, so they're read from TsvrcConfig - a scene
    // component ScaffoldModule auto-creates/heals under TsvrcGenerated, not an asset (an asset
    // cannot hold a reference to a scene object: no stable cross-file address for it). Builtin/
    // library-internal singletons are expected to be asset-type objects, so those still come
    // from TsvrcBuiltinConfig (which is a genuine asset).
    //
    // Deliberately does not replicate V1's call-site-stub feature (every configured singleton
    // is always a real field). Generates _TsSingletonStart() which calls TsConstruct(this) on
    // any singleton that is a TsvrcBehaviour.
    internal class SingletonModule : TsvrcModule
    {
        private List<SingletonEntry> _entries = new List<SingletonEntry>();

        internal override string FileName => "TsvrcGeneratedSingleton.cs";

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
            var sceneConfig = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsvrcBuiltinConfig>(BuiltinConfigPath);

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

            _entries = Resolve(combined);
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

        private static string BuildStub()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            using (w.Method("public void _TsSingletonStart()"))
            { }
            return w.ToString();
        }

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            var so = new SerializedObject(root);
            foreach (var entry in _entries)
            {
                var prop = so.FindProperty(entry.Name);
                if (prop == null)
                {
                    Debug.LogWarning($"[SingletonModule] Field '{entry.Name}' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
                    continue;
                }
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
                if (obj == null)
                {
                    Debug.LogWarning("[SingletonModule] Null entry in config, remove the missing-script slot.");
                    continue;
                }
                if (!seen.Add(obj))
                {
                    Debug.LogWarning($"[SingletonModule] Duplicate entry '{obj.name}' in config, remove the duplicate.");
                    continue;
                }

                var type = obj.GetType();
                string goName = obj is Component c ? c.gameObject.name : (obj is GameObject go ? go.name : string.Empty);
                string baseName = DeriveName(type, goName);
                string name = Deduplicate(baseName, usedNames);
                usedNames.Add(name);

                entries.Add(new SingletonEntry
                {
                    Name = name,
                    TypeName = type.Name,
                    Namespace = type.Namespace ?? string.Empty,
                    SourceObject = obj,
                });
            }

            return entries;
        }

        private static string DeriveName(Type type, string goName)
        {
            if (type == typeof(Animator))
                return (AliasName(goName) ?? goName) + "Animator";
            return AliasName(goName) ?? type.Name;
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
