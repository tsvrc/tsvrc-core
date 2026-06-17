#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
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
    // is always a real field) or its TsConstruct/TsStart invocation (V1's SingletonModule keeps
    // running in parallel and remains the sole caller of those until it is retired - same
    // reasoning as InstanceModule's existing deferral).
    internal class SingletonModule : TsvrcModule
    {
        private const string BuiltinConfigPath = "Assets/Tsvrc/TsvrcBuiltinConfig.asset";

        private List<SingletonEntry> _entries = new List<SingletonEntry>();

        internal override string FileName => "TsvrcSingletonBehaviour.cs";

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override void LoadConfig()
        {
            var sceneConfig = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsvrcBuiltinConfig>(BuiltinConfigPath);

            var combined = (sceneConfig?.Singletons ?? Array.Empty<UnityEngine.Object>())
                .Union(builtinConfig?.Singletons ?? Array.Empty<UnityEngine.Object>());

            _entries = Resolve(combined);
        }

        internal override string GenerateCode()
        {
            if (_entries.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UdonSharp", "UnityEngine" };
            foreach (var entry in _entries)
                if (!string.IsNullOrEmpty(entry.Namespace) && !usings.Contains(entry.Namespace))
                    usings.Add(entry.Namespace);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.SingletonClassName} : UdonSharpBehaviour"))
            {
                foreach (var entry in _entries.OrderBy(e => e.Name))
                {
                    w.Summary("Tsvrc singleton.");
                    w.Line($"[HideInInspector] [SerializeField] public {entry.TypeName} {entry.Name};");
                }

                using (w.Method("void Start()")) { }
            }

            return w.ToString();
        }

        private static string BuildStub()
        {
            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.SingletonClassName} : UdonSharpBehaviour"))
            using (w.Method("void Start()"))
            { }
            return w.ToString();
        }

        internal override void Wire()
        {
            var singletonType = ScaffoldModule.FindSingletonType();
            if (singletonType == null) return;

            var singletonComp = (Component)UnityEngine.Object.FindObjectOfType(singletonType, true);
            if (singletonComp == null) return;

            var so = new SerializedObject(singletonComp);
            foreach (var entry in _entries)
            {
                var prop = so.FindProperty(entry.Name);
                if (prop == null)
                {
                    Debug.LogWarning($"[SingletonModule] Field '{entry.Name}' not found on {ScaffoldModule.SingletonClassName}. Force compile to regenerate.");
                    continue;
                }
                prop.objectReferenceValue = entry.SourceObject;
            }

            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(singletonComp.gameObject.scene);
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

        // __Foo__ on the GameObject overrides the generated field name to Foo.
        private static string AliasName(string goName)
        {
            if (goName != null && goName.StartsWith("__") && goName.EndsWith("__") && goName.Length > 4)
                return goName.Substring(2, goName.Length - 4);
            return null;
        }

        private static string Deduplicate(string baseName, HashSet<string> usedNames)
        {
            string name = baseName;
            int suffix = 2;
            while (usedNames.Contains(name))
                name = $"{baseName}{suffix++}";
            return name;
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
