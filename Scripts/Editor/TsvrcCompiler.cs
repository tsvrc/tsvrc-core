#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    public static class TsvrcCompiler
    {
        internal const string PendingWireKey  = "TsvrcPendingWire";
        private  const string AccessorPath    = "Assets/TsvrcGenerated/CompiledTsvrc.cs";
        private  const string InstancePath    = "Assets/TsvrcGenerated/CompiledTsvrcInstance.cs";

        [MenuItem("Tsvrc/Compile")]
        public static void Compile()
        {
            string root = Path.GetDirectoryName(Application.dataPath);

            string accessorFull  = Full(root, AccessorPath);
            string instanceFull  = Full(root, InstancePath);

            var result = TsvrcScanner.Scan();
            if (result == null) return;

            Directory.CreateDirectory(Path.GetDirectoryName(accessorFull));
            File.WriteAllText(accessorFull,  TsvrcCodeBuilder.Build(result),         Encoding.UTF8);
            File.WriteAllText(instanceFull,  TsvrcCodeBuilder.BuildInstance(result),  Encoding.UTF8);

            EditorPrefs.SetBool(PendingWireKey, true);
            AssetDatabase.Refresh();

            // If the generated files were unchanged Unity skips recompile — [DidReloadScripts]
            // never fires and WireScene never runs. Schedule it here unconditionally so
            // re-running Compile always rewires the scene even without a recompile.
            EditorApplication.delayCall += TsvrcSceneWirer.WireScene;

            Debug.Log($"[TsvrcCompiler] Wrote '{AccessorPath}' + '{InstancePath}' — {result.TotalEntries()} entries. Scene objects will be wired after Unity recompiles.");
        }

        private static string Full(string root, string assetPath) =>
            Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
#endif
