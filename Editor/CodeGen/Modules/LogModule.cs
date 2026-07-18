#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Generates the _log field and _TsLogStart() on TsGenerated, and wires the scene
    // TsLogger component into it. Every other system accesses logging through
    // TsGenerated.Log (or the TsBehaviour.LogInfo/LogWarning/LogError wrappers); this
    // module ensures that reference is always set. Mirrors MemoryModule.
    internal class LogModule : TsModule
    {
        // Package-relative, not a literal - see PackagePaths.
        private static string LogScriptPath => $"{PackagePaths.Root}/Runtime/Utils/TsLogger.cs";
        private static string LogAssetPath => $"{PackagePaths.Root}/Runtime/Utils/TsLogger.asset";

        internal override string FileName => "TsGeneratedLog.cs";

        internal override IEnumerable<string> WatchedAssets() => new[] { LogAssetPath };

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
                w.Line("[ReadOnly] [SerializeField] private TsLogger _log;");
                w.BlankLine();
                w.Line("public override TsLogger Log => _log;");
                w.BlankLine();
                using (w.Method("public void _TsLogStart()"))
                    w.Line("_log.TsConstruct(this);");
            }
            return w.ToString();
        }

        internal override bool AfterFilesStable()
        {
            bool programAssetMissing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LogAssetPath) == null;
            ScaffoldModule.EnsureUdonSharpProgramAsset(LogScriptPath, LogAssetPath);

            var root = FindRoot();
            if (root == null) return programAssetMissing;

            ScaffoldModule.EnsureChildSceneObject("TsLogger", typeof(TsLogger), root);
            return programAssetMissing;
        }

        internal override void Wire()
        {
            var root = FindRoot();
            if (root == null) return;

            var log = (Component)UnityEngine.Object.FindObjectOfType(typeof(TsLogger), true);

            var so = new SerializedObject(root);
            var prop = so.FindProperty("_log");
            if (prop == null)
            {
                Debug.LogWarning($"[LogModule] Field '_log' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
                return;
            }
            if (prop.objectReferenceValue == (UnityEngine.Object)log) return;
            prop.objectReferenceValue = log;
            ApplyAndMarkDirty(so, root);
        }

        internal override bool OnSceneHierarchyChanged()
        {
            var root = FindRoot();
            if (root == null) return false;
            return root.transform.Find("TsLogger") == null;
        }
    }
}
#endif
