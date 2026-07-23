#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Tsvrc.Core;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Handles the single TsInstance for the world. There is no manual override: the module
    // scans loaded assemblies for a TsInstance subclass and fully owns a single child object
    // named "TsInstance" under TsGenerated, creating/repairing/removing it as needed so the
    // wiring is self-recovering without any user action.
    //
    // Generates _TsInstanceStart() which calls TsConstruct(this) then OnInstanceStart() on the
    // resolved instance.
    internal class InstanceModule : TsModule
    {
        private const string ChildName = "TsInstance";
        private const string FieldName = "_instance";
        private const string GeneratedFolder = "Assets/TsGenerated";

        private Type _detectedType;
        private bool _ambiguous;

        internal override string FileName => "TsGeneratedInstance.cs";

        internal override IEnumerable<string> WatchedAssets()
        {
            if (_detectedType != null)
                yield return $"{GeneratedFolder}/{_detectedType.Name}.asset";
        }

        internal override string GenerateCode()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "Tsvrc.Core", "Tsvrc.Utils", "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                w.Line("[ReadOnly] [SerializeField] private TsInstance _instance;");
                w.BlankLine();
                w.Line("public override TsInstance Instance => _instance;");
                w.BlankLine();
                using (w.Method("public void _TsInstanceStart()"))
                {
                    w.Line("_instance.TsConstruct(this);");
                    w.Line("_instance.OnInstanceStart();");
                }
            }
            return w.ToString();
        }

        internal override void LoadConfig()
        {
            (_detectedType, _ambiguous) = DetectInstanceType();
        }

        // Only relevant when a single candidate type is resolved: if the child we created/repaired
        // last Wire() pass was deleted by hand, trigger a rerun so it gets recreated. Ambiguous or
        // empty detection results never had a child they're entitled to recreate.
        internal override bool OnSceneHierarchyChanged()
        {
            if (_ambiguous || _detectedType == null) return false;

            var root = FindRoot();
            if (root == null) return false;

            return root.transform.Find(ChildName) == null;
        }

        internal override void Wire()
        {
            // Ambiguous: leave whatever is currently wired alone until the project is back down
            // to at most one TsInstance subclass. Destroying a working setup because a second,
            // possibly transient, subclass appeared would be worse than doing nothing.
            if (_ambiguous) return;

            var root = FindRoot();
            if (root == null) return;

            var existingChild = root.transform.Find(ChildName);

            if (_detectedType == null)
            {
                if (existingChild != null)
                    Undo.DestroyObjectImmediate(existingChild.gameObject);
                SetInstanceField(root, null);
                return;
            }

            GameObject childGo;
            UdonSharpBehaviour component;

            if (existingChild == null)
            {
                childGo = new GameObject(ChildName);
                Undo.RegisterCreatedObjectUndo(childGo, $"Create {ChildName}");
                childGo.transform.SetParent(root.transform, false);
                component = CreateComponent(childGo, _detectedType);
            }
            else
            {
                childGo = existingChild.gameObject;
                component = childGo.GetComponent(_detectedType) as UdonSharpBehaviour;

                string programAssetPath = $"{GeneratedFolder}/{_detectedType.Name}.asset";
                bool programAssetMissing = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programAssetPath) == null;

                // Recreate when: wrong/stale component type (renamed or manually replaced),
                // OR the program asset was deleted — in that case the backing UdonBehaviour's
                // programSource is null and UdonSharp's sanitize pass will error on next compile.
                // UdonSharpUndo.DestroyImmediate is required (not plain Undo) because UdonSharp
                // components carry a hidden backing UdonBehaviour that a plain destroy would orphan.
                if (component == null || programAssetMissing)
                {
                    foreach (var stale in childGo.GetComponents<UdonSharpBehaviour>())
                        UdonSharpUndo.DestroyImmediate(stale);
                    component = CreateComponent(childGo, _detectedType);
                }
            }

            if (component == null) return; // creation failed; already logged by CreateComponent

            SetInstanceField(root, component);
        }

        private static void SetInstanceField(Component root, UdonSharpBehaviour value)
        {
            var so = new SerializedObject(root);
            var prop = so.FindProperty(FieldName);
            if (prop == null)
            {
                Debug.LogWarning($"[InstanceModule] Field '{FieldName}' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
                return;
            }

            if (prop.objectReferenceValue == (UnityEngine.Object)value) return;

            prop.objectReferenceValue = value;
            ApplyAndMarkDirty(so, root);
        }

        private static UdonSharpBehaviour CreateComponent(GameObject go, Type type)
        {
            string scriptAssetPath = FindScriptAssetPath(type.Name);
            if (string.IsNullOrEmpty(scriptAssetPath))
            {
                Debug.LogWarning($"[InstanceModule] Could not locate script asset for '{type.Name}'.");
                return null;
            }

            string programAssetPath = $"{GeneratedFolder}/{type.Name}.asset";
            if (!ScaffoldModule.EnsureUdonSharpProgramAsset(scriptAssetPath, programAssetPath))
            {
                Debug.LogWarning($"[InstanceModule] Could not create program asset for '{type.Name}'.");
                return null;
            }

            return UdonSharpUndo.AddComponent(go, type);
        }

        // Returns (type, ambiguous). type is null when there are zero or more than one candidates;
        // ambiguous distinguishes "nothing to wire" from "leave the current wiring alone".
        //
        // Deduplicated by full name: Unity's AppDomain can carry stale duplicate copies of the same
        // assembly across successive recompiles, which would otherwise make a single real subclass
        // look "ambiguous" just because it was seen twice. Types marked [TsCodegenIgnore] (e.g. a
        // test double TsInstance subclass) are excluded from this scan the same way as
        // TsGenerator.HasBootstrapSignal, so one never gets treated as the one real scaffold to
        // wire, and never falsely trips the "multiple subclasses" ambiguity error against a real one.
        private static (Type, bool) DetectInstanceType()
        {
            var candidates = new List<Type>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var type in types)
                    if (type != typeof(TsInstance) && !type.IsAbstract && typeof(TsInstance).IsAssignableFrom(type)
                        && type.GetCustomAttribute<TsCodegenIgnoreAttribute>() == null)
                        if (seen.Add(type.FullName))
                            candidates.Add(type);
            }

            if (candidates.Count > 1)
            {
                Debug.LogError($"[InstanceModule] Multiple TsInstance subclasses found ({string.Join(", ", candidates.Select(t => t.Name))}). " +
                    "Exactly one is required; leaving the current wiring untouched until this is resolved.");
                return (null, true);
            }

            return (candidates.Count == 1 ? candidates[0] : null, false);
        }

        private static string FindScriptAssetPath(string typeName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {typeName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == typeName)
                    return path;
            }
            return null;
        }
    }
}
#endif
