#if UNITY_EDITOR
using System.IO;
using System.Text;
using Tsvrc.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class TsvrcCompiler
    {
        private const string GeneratedFolder = "Assets/CompiledTsvrc";
        private const string AccessorPath = "Assets/CompiledTsvrc/CompiledTsvrc.cs";
        private const string TsvrcConfigPrefabPath = "Assets/Tsvrc/Prefabs/TsvrcConfig.prefab";

        [MenuItem("Tsvrc/Compile")]
        public static void Compile()
        {
            var config = RequireTsvrcConfig();
            if (config == null) return;

            string root = Path.GetDirectoryName(Application.dataPath);
            string folderFull = ToAbsolutePath(root, GeneratedFolder);
            string accessorFull = ToAbsolutePath(root, AccessorPath);

            if (Directory.Exists(folderFull))
                Directory.Delete(folderFull, true);
            Directory.CreateDirectory(folderFull);

            var results = TsvrcScanner.Scan(config);
            var compiled = TsvrcBuilder.Build(config, results);

            File.WriteAllText(accessorFull, compiled, Encoding.UTF8);

            AssetDatabase.Refresh();
        }

        private static string ToAbsolutePath(string root, string assetPath)
        {
            return Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static TsvrcConfig RequireTsvrcConfig()
        {
            var all = Object.FindObjectsOfType<TsvrcConfig>();

            switch (all.Length)
            {
                case > 1:
                    Debug.LogError("Multiple TsvrcConfig found — remove duplicates and recompile.");
                    return null;

                case 1:
                    return all[0]; // already good

                default: // 0 — create it
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TsvrcConfigPrefabPath);
                    if (prefab == null)
                    {
                        Debug.LogError($"TsvrcConfig prefab not found at '{TsvrcConfigPrefabPath}'.");
                        return null;
                    }

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.transform.SetSiblingIndex(0);
                    Undo.RegisterCreatedObjectUndo(go, "Add TsvrcConfig");
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

                    return go.GetComponent<TsvrcConfig>();
            }
        }
    }
}
#endif
