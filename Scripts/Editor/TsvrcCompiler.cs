#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    public static class TsvrcCompiler
    {
        internal const string PendingWireKey = "TsvrcPendingWire";

        private const string AccessorPath = "Assets/TsvrcGenerated/CompiledTsvrc.cs";
        private const string InstancePath = "Assets/TsvrcGenerated/CompiledTsvrcConfig.cs";

        [MenuItem("Tsvrc/Compile")]
        public static void Compile()
        {
            var result = TsvrcScanner.Scan();
            if (result == null) return;

            string root = Path.GetDirectoryName(Application.dataPath);
            string accessorFull = ToAbsolutePath(root, AccessorPath);
            string instanceFull = ToAbsolutePath(root, InstancePath);

            Directory.CreateDirectory(Path.GetDirectoryName(accessorFull));
            File.WriteAllText(accessorFull, TsvrcCompiledBuilder.Build(result), Encoding.UTF8);
            File.WriteAllText(instanceFull, TsvrcInstanceBuilder.Build(result), Encoding.UTF8);

            EditorPrefs.SetBool(PendingWireKey, true);
            AssetDatabase.Refresh();

            // Schedule WireScene unconditionally — handles the case where Unity skips recompile.
            EditorApplication.delayCall += TsvrcSceneWirer.WireScene;

            Debug.Log(
                $"[TsvrcCompiler] Wrote '{AccessorPath}' + '{InstancePath}' \u2014 " +
                $"{result.TotalEntries()} entries. Scene objects will be wired after Unity recompiles.");
        }

        private static string ToAbsolutePath(string root, string assetPath) =>
            Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
#endif
