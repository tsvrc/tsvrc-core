#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    public static class TsvrcCompiler
    {
        private const string OutputPath = "Assets/TsvrcGenerated/CompiledTsvrc.cs";

        [MenuItem("Tsvrc/Compile Singletons")]
        public static void Compile()
        {
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                OutputPath.Replace('/', Path.DirectorySeparatorChar)
            );

            var result = TsvrcScanner.Scan(fullPath);
            if (result == null) return;

            string source = TsvrcCodeBuilder.Build(result);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, source, Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log($"[TsvrcCompiler] Generated '{OutputPath}' — {result.TotalEntries()} entries across {result.Groups.Count} group(s).");
        }
    }
}
#endif
