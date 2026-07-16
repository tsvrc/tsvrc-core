#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates the _memory field and _TsMemoryStart() on TsGenerated, and wires
    // the scene TsMemory component into it. Every other system accesses shared memory
    // through TsGenerated.Memory; this module ensures that reference is always set.
    internal class MemoryModule : TsModule
    {
        // Package-relative, not a literal - see PackagePaths.
        private static string MemoryScriptPath => $"{PackagePaths.Root}/Runtime/Utils/TsMemory.cs";
        private static string MemoryAssetPath => $"{PackagePaths.Root}/Runtime/Utils/TsMemory.asset";

        internal override string FileName => "TsGeneratedMemory.cs";

        internal override IEnumerable<string> WatchedAssets() => new[] { MemoryAssetPath };

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
                w.Line("[ReadOnly] [SerializeField] private TsMemory _memory;");
                w.BlankLine();
                w.Line("public override TsMemory Memory => _memory;");
                w.BlankLine();
                using (w.Method("public void _TsMemoryStart()"))
                    w.Line("_memory.TsConstruct(this);");
            }
            return w.ToString();
        }

        internal override bool AfterFilesStable()
        {
            bool programAssetMissing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(MemoryAssetPath) == null;
            ScaffoldModule.EnsureUdonSharpProgramAsset(MemoryScriptPath, MemoryAssetPath);

            var root = FindRoot();
            if (root == null) return programAssetMissing;

            ScaffoldModule.EnsureChildSceneObject("TsMemory", typeof(TsMemory), root);
            return programAssetMissing;
        }

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            var memory = (Component)UnityEngine.Object.FindObjectOfType(typeof(TsMemory), true);

            var so = new SerializedObject(root);
            var prop = so.FindProperty("_memory");
            if (prop == null)
            {
                Debug.LogWarning($"[MemoryModule] Field '_memory' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
                return;
            }
            if (prop.objectReferenceValue == (UnityEngine.Object)memory) return;
            prop.objectReferenceValue = memory;
            ApplyAndMarkDirty(so, root);
        }

        internal override bool OnSceneHierarchyChanged()
        {
            var root = FindRoot();
            if (root == null) return false;
            return root.transform.Find("TsMemory") == null;
        }
    }
}
#endif
