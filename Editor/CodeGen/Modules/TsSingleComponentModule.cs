#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Shared shape for a module that generates exactly one [ReadOnly][SerializeField] field on
    // TsGenerated, backed by one library-internal singleton component that this module creates
    // and heals in the scene (LogModule/TsvrcLogger, MemoryModule/TsvrcMemory): identical
    // GenerateCode/AfterFilesStable/Wire/OnSceneHierarchyChanged, differing only in which
    // component type, field/method names, and script/asset paths are involved.
    internal abstract class TsSingleComponentModule : TsModule
    {
        protected abstract Type ComponentType { get; }
        protected abstract string FieldName { get; }
        protected abstract string PublicPropertyName { get; }
        protected abstract string StartMethodName { get; }
        protected abstract string ChildGameObjectName { get; }
        protected abstract string ScriptPath { get; }
        protected abstract string AssetPath { get; }
        protected abstract string ModuleTag { get; }

        internal override IEnumerable<string> WatchedAssets() => new[] { AssetPath };

        internal override void LoadConfig() { }

        internal override string GenerateCode()
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "Tsvrc.Utils", "UdonSharp", "UnityEngine" });
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
            {
                w.Line($"[ReadOnly] [SerializeField] private {ComponentType.Name} {FieldName};");
                w.BlankLine();
                w.Line($"public override {ComponentType.Name} {PublicPropertyName} => {FieldName};");
                w.BlankLine();
                using (w.Method($"public void {StartMethodName}()"))
                    w.Line($"{FieldName}.TsConstruct(this);");
            }
            return w.ToString();
        }

        internal override bool AfterFilesStable()
        {
            bool programAssetMissing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetPath) == null;
            ScaffoldModule.EnsureUdonSharpProgramAsset(ScriptPath, AssetPath);

            var root = FindRoot();
            if (root == null) return programAssetMissing;

            ScaffoldModule.EnsureChildSceneObject(ChildGameObjectName, ComponentType, root);
            return programAssetMissing;
        }

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            // Scoped to the linked scene, same as FindRoot() above, so an unrelated
            // TsvrcLogger/TsvrcMemory instance in an additively-loaded scene never gets wired
            // into this scene's TsGenerated field.
            var component = TsLinkedScene.FindType(ComponentType);

            var so = new SerializedObject(root);
            if (!TryFindField(so, FieldName, ModuleTag, out var prop)) return;
            if (prop.objectReferenceValue == (UnityEngine.Object)component) return;
            prop.objectReferenceValue = component;
            ApplyAndMarkDirty(so, root);
        }

        internal override bool OnSceneHierarchyChanged()
        {
            var root = FindRoot();
            if (root == null) return false;
            return root.transform.Find(ChildGameObjectName) == null;
        }
    }
}
#endif
