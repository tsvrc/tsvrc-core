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

            Debug.Log($"[TsvrcCompiler] Wrote '{AccessorPath}' + '{InstancePath}' — {result.TotalEntries()} entries. Scene wiring queued.");
        }

        private static string Full(string root, string assetPath) =>
            Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
#endif
