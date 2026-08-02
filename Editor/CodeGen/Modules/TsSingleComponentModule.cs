#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Config;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Shared shape for a module that generates exactly one [ReadOnly][SerializeField] field on
    // TsGenerated, backed by one library-internal singleton component that this module creates
    // and heals in the scene (LogModule/TsvrcLogger, MemoryModule/TsvrcMemory): identical
    // GenerateCode/AfterFilesStable/Wire/OnSceneHierarchyChanged, differing only in which
    // component type, field/method names, and script/asset paths are involved.
    //
    // ComponentType's own .cs/.asset (TsvrcLogger.cs/.asset, TsvrcMemory.cs/.asset) are permanent,
    // self-healing package resources this module never creates or deletes; EnsureUdonSharpProgramAsset
    // only heals them. TsRoot.Log/Memory are declared `virtual ... => null`, not abstract, so
    // omitting this module's field+property override is a safe, ordinary compile: a caller reading
    // _ts.Log/_ts.Memory while unused simply gets TsRoot's own null default. Usage only gates two
    // things: the GenerateCode() fragment and the scene child GameObject's existence.
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

        // Additional bare method-call sites (e.g. LogModule's LogInfo/LogWarning/LogError, the
        // TsvrcBehaviour convenience wrappers that reach _ts.Log indirectly) that also count as
        // "this subsystem is used," beyond the direct _ts.{PublicPropertyName} member access every
        // subclass already checks. Empty by default (MemoryModule has no such wrapper).
        protected virtual IEnumerable<string> AdditionalUsageMethodNames() => Enumerable.Empty<string>();

        private bool _isUsed = true;
        private List<string> _lastExcluded = new List<string>();
        private List<string> _lastGraceIncluded = new List<string>();

        // Read by LogModule.DrawTab so its empty-state message can distinguish "never generated
        // yet, Force Regenerate will create it" from "tree-shaken away, won't reappear until
        // referenced or force-included" - otherwise the tab's own guidance would be actively wrong
        // once tree-shaking is on and this subsystem is legitimately unused.
        protected bool IsUsed => _isUsed;

        internal override IEnumerable<string> LastTreeShakingExclusions => _lastExcluded;
        internal override IEnumerable<string> LastTreeShakingGraceIncluded => _lastGraceIncluded;

        internal override IEnumerable<string> WatchedAssets() => new[] { AssetPath };

        // Reuses ApplyTreeShaking against a single-element list rather than hand-rolling its own
        // grace-period bookkeeping: this subsystem either is or isn't "used" as a whole, which is
        // exactly what a one-entry list resolves to.
        internal override void LoadConfig()
        {
            var config = TsLinkedScene.Find<TsConfig>();

            var kept = ApplyTreeShaking(config, ModuleTag, new List<string> { PublicPropertyName },
                name => name, IsReferencedByProjectSource,
                out _, out _lastExcluded, out _lastGraceIncluded);

            _isUsed = kept.Count > 0;
        }

        private bool IsReferencedByProjectSource(string name)
        {
            if (TsUsageScanner.IsMemberReferenced(name)) return true;
            foreach (var methodName in AdditionalUsageMethodNames())
                if (TsUsageScanner.IsMethodCallReferenced(methodName)) return true;
            return false;
        }

        internal override string GenerateCode()
        {
            if (!_isUsed)
                return BuildStub(new[] { "Tsvrc.Utils", "UdonSharp", "UnityEngine" }, $"public void {StartMethodName}()");

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
            // ComponentType's own script/asset are permanent package resources (see class doc
            // comment) - always healed regardless of usage, exactly like a registered Pool
            // prefab's own program asset is never gated by PoolModule.
            bool programAssetMissing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetPath) == null;
            ScaffoldModule.EnsureUdonSharpProgramAsset(ScriptPath, AssetPath);

            var root = FindRoot();
            if (root == null) return programAssetMissing;

            var existingChild = root.transform.Find(ChildGameObjectName);
            if (!_isUsed)
            {
                // Mirrors FactoryModule.Wire()'s empty-container teardown: only the scene instance
                // goes away, never ComponentType's own script/asset.
                if (existingChild != null)
                    Undo.DestroyObjectImmediate(existingChild.gameObject);
                return programAssetMissing;
            }

            ScaffoldModule.EnsureChildSceneObject(ChildGameObjectName, ComponentType, root);
            return programAssetMissing;
        }

        internal override void Wire()
        {
            // No field exists on the compiled type to wire while unused (GenerateCode() above
            // omitted it) - TryFindField's "force compile to regenerate" warning would otherwise
            // fire every pass for a field that's deliberately, permanently absent, not stale.
            if (!_isUsed) return;

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
            bool childExists = root.transform.Find(ChildGameObjectName) != null;
            // Used-but-missing needs a rerun to create it; unused-but-still-present (e.g. usage
            // was just removed from source, or tree-shaking was just turned on) needs a rerun to
            // tear it down. Either mismatch between _isUsed and the scene's actual state is a
            // reason to run again; agreement in either direction is not.
            return _isUsed ? !childExists : childExists;
        }
    }
}
#endif
