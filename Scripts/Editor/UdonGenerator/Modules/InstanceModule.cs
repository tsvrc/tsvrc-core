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
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    // Handles the single TsvrcInstance for the world. There is no manual override: the module
    // scans loaded assemblies for a TsvrcInstance subclass and fully owns a single child object
    // named "TsvrcInstance" under TsvrcGenerated, creating/repairing/removing it as needed so the
    // wiring is self-recovering without any user action. See InstanceModule-Design.md for the
    // full scenario analysis behind these choices.
    //
    // Generates _TsInstanceStart() which calls TsConstruct(this) then OnInstanceStart() on the
    // resolved instance.
    internal class InstanceModule : TsvrcModule
    {
        private const string ChildName = "TsvrcInstance";
        private const string FieldName = "_instance";
        private const string GeneratedFolder = "Assets/TsvrcGenerated";

        private Type _detectedType;
        private bool _ambiguous;

        internal override string FileName => "TsvrcGeneratedInstance.cs";

        internal override string GenerateCode()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "Tsvrc.Core", "Tsvrc.Utils", "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                w.Line("[ReadOnly] [SerializeField] private TsvrcInstance _instance;");
                w.BlankLine();
                w.Line("public TsvrcInstance Instance => _instance;");
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
            // to at most one TsvrcInstance subclass. Destroying a working setup because a second,
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

                // Wrong/stale component (type renamed, or manually removed and something else
                // added): drop whatever UdonSharpBehaviours are sitting on it and add the right
                // one. UdonSharpUndo.DestroyImmediate is required here (not a plain Undo destroy)
                // because UdonSharp components have a hidden backing UdonBehaviour that a plain
                // destroy would orphan.
                if (component == null)
                {
                    foreach (var stale in childGo.GetComponents<UdonSharpBehaviour>())
                        UdonSharpUndo.DestroyImmediate(stale);
                    component = CreateComponent(childGo, _detectedType);
                }
            }

            if (component == null) return; // creation failed; already logged by CreateComponent

            SetInstanceField(root, component);
        }

        private static Component FindRoot()
        {
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType == null) return null;
            return (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
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
            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
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
            if (!EnsureUdonSharpProgramAsset(scriptAssetPath, programAssetPath))
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
        // look "ambiguous" just because it was seen twice.
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
                    if (type != typeof(TsvrcInstance) && !type.IsAbstract && typeof(TsvrcInstance).IsAssignableFrom(type))
                        if (seen.Add(type.FullName))
                            candidates.Add(type);
            }

            if (candidates.Count > 1)
            {
                Debug.LogError($"[InstanceModule] Multiple TsvrcInstance subclasses found ({string.Join(", ", candidates.Select(t => t.Name))}). " +
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

        private static bool EnsureUdonSharpProgramAsset(string scriptPath, string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                return true;

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (monoScript == null) return false;

            var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = monoScript;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            AssetDatabase.SaveAssetIfDirty(programAsset);
            return true;
        }
    }
}
#endif
