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

        private const string GeneratedFolder = "Assets/TsvrcGenerated";
        private const string AccessorPath = "Assets/TsvrcGenerated/CompiledTsvrc.cs";

        [MenuItem("Tsvrc/Compile")]
        public static void Compile()
        {
            var result = TsvrcScanner.Scan();
            if (result == null) return;

            string root = Path.GetDirectoryName(Application.dataPath);
            string folderFull = ToAbsolutePath(root, GeneratedFolder);
            string accessorFull = ToAbsolutePath(root, AccessorPath);

            // Wipe only generated .cs files, preserving .asset files so
            // UdonBehaviour program references remain valid across compiles.
            if (Directory.Exists(folderFull))
                foreach (var f in Directory.GetFiles(folderFull, "*.cs"))
                    File.Delete(f);
            else
                Directory.CreateDirectory(folderFull);

            File.WriteAllText(accessorFull, TsvrcCompiledBuilder.Build(result), Encoding.UTF8);

            // Delete any .asset files that are no longer expected (e.g. renamed instance type).
            var expectedAssets = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            expectedAssets.Add(Path.GetFileNameWithoutExtension(accessorFull) + ".asset");
            if (result.InstanceType != null)
                expectedAssets.Add(result.InstanceType.Name + ".asset");
            foreach (var asset in Directory.GetFiles(folderFull, "*.asset"))
            {
                if (!expectedAssets.Contains(Path.GetFileName(asset)))
                    File.Delete(asset);
            }
            EditorPrefs.SetBool(PendingWireKey, true);
            AssetDatabase.Refresh();

            Debug.Log(
                $"[TsvrcCompiler] Wrote '{AccessorPath}' \u2014 " +
                $"{result.TotalEntries()} entries. Scene objects will be wired after Unity recompiles.");
        }

        private static string ToAbsolutePath(string root, string assetPath) =>
            Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
#endif
