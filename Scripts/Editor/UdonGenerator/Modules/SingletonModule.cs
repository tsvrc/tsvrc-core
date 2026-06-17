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

        private List<TsvrcField> _currentFields = new List<TsvrcField>();

        internal override string FileName => "TsvrcSingletonBehaviour.cs";

        internal override IEnumerable<string> WatchedAssets() => new[] { BuiltinConfigPath };

        internal override void LoadConfig()
        {
            var sceneConfig = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            var builtinConfig = AssetDatabase.LoadAssetAtPath<TsvrcBuiltinConfig>(BuiltinConfigPath);

            var combined = (sceneConfig?.Singletons ?? Array.Empty<UnityEngine.Object>())
                .Union(builtinConfig?.Singletons ?? Array.Empty<UnityEngine.Object>());

            _currentFields = TsvrcResolver.Resolve(combined);
        }

        internal override string GenerateCode()
        {
            if (_currentFields.Count == 0)
                return BuildStub();

            var usings = new List<string> { "UdonSharp", "UnityEngine" };
            foreach (var field in _currentFields)
                if (!string.IsNullOrEmpty(field.Namespace) && !usings.Contains(field.Namespace))
                    usings.Add(field.Namespace);

            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(usings);

            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public class {ScaffoldModule.SingletonClassName} : UdonSharpBehaviour"))
            {
                foreach (var field in _currentFields.OrderBy(f => f.Name))
                {
                    w.Summary("Tsvrc singleton.");
                    w.Line($"[HideInInspector] [SerializeField] public {field.Type} {field.Name};");
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
            foreach (var field in _currentFields)
            {
                var prop = so.FindProperty(field.Name);
                if (prop == null)
                {
                    Debug.LogWarning($"[SingletonModule] Field '{field.Name}' not found on {ScaffoldModule.SingletonClassName}. Force compile to regenerate.");
                    continue;
                }
                prop.objectReferenceValue = field.SourceObject;
            }

            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(singletonComp.gameObject.scene);
        }
    }
}
#endif
