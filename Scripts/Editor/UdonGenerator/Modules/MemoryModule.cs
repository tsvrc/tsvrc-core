#if UNITY_EDITOR
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates the _memory field and _TsMemoryStart() on TsvrcGenerated, and wires
    // the scene TsMemory component into it. Every other system accesses shared memory
    // through TsvrcGenerated.Memory; this module ensures that reference is always set.
    internal class MemoryModule : TsvrcModule
    {
        private const string MemoryScriptPath = "Assets/Tsvrc/Scripts/Utils/TsMemory.cs";
        private const string MemoryAssetPath = "Assets/Tsvrc/Scripts/Utils/TsMemory.asset";

        internal override string FileName => "TsvrcGeneratedMemory.cs";

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
                w.Line("public TsMemory Memory => _memory;");
                w.BlankLine();
                using (w.Method("public void _TsMemoryStart()"))
                    w.Line("_memory.TsConstruct(this);");
            }
            return w.ToString();
        }

        internal override bool AfterFilesStable()
        {
            ScaffoldModule.EnsureUdonSharpProgramAsset(MemoryScriptPath, MemoryAssetPath);

            var root = FindRoot();
            if (root == null) return false;

            ScaffoldModule.EnsureChildSceneObject("TsvrcMemory", typeof(TsMemory), root);
            return false;
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
            return root.transform.Find("TsvrcMemory") == null;
        }
    }
}
#endif
