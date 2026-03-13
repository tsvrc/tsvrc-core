#if UNITY_EDITOR
using System.IO;
using System.Text;
using Tsvrc.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Editor
{

    public static class TsvrcCompilerV2
    {
        private const string GeneratedFolder = "Assets/CompiledTsvrc";
        private const string AccessorPath = "Assets/CompiledTsvrc/CompiledTsvrc.cs";
        private const string TsvrcConfigPrefabPath = "Assets/Tsvrc/Prefabs/TsvrcConfig.prefab";

        [MenuItem("Tsvrc/Compile V2")]
        public static void Compile()
        {
            EnsureTsvrcConfigInScene();

            string root = Path.GetDirectoryName(Application.dataPath);
            string folderFull = ToAbsolutePath(root, GeneratedFolder);
            string accessorFull = ToAbsolutePath(root, AccessorPath);

            if (Directory.Exists(folderFull))
                Directory.Delete(folderFull, true);

            Directory.CreateDirectory(folderFull);

            AssetDatabase.Refresh();
        }

        private static string ToAbsolutePath(string root, string assetPath)
        {
            return Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void EnsureTsvrcConfigInScene()
        {
            var existing = Object.FindObjectsOfType<TsvrcConfig>();

            if (existing.Length > 1)
            {
                Debug.LogError("[TsvrcCompiler] Multiple TsvrcConfig found in the scene. Remove the duplicates and compile again.");
                return;
            }

            if (existing.Length == 1)
                return; // already present

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TsvrcConfigPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[TsvrcCompiler] TsvrcConfig prefab not found at '{TsvrcConfigPrefabPath}'.");
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(go, "Add TsvrcConfig");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[TsvrcCompiler] TsvrcConfig prefab added to scene.");
        }
    }

    public static class TsvrcCompiler
    {
        internal const string PendingWireKey = "TsvrcPendingWire";

        private const string GeneratedFolder = "Assets/TsvrcGenerated";
        private const string AccessorPath = "Assets/TsvrcGenerated/CompiledTsvrc.cs";

        private const string TsvrcConfigPrefabPath = "Assets/Tsvrc/Prefabs/TsvrcConfig.prefab";

        [MenuItem("Tsvrc/Compile")]
        public static void Compile()
        {
            EnsureTsvrcConfigInScene();

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

        private static void EnsureTsvrcConfigInScene()
        {
            var existing = UnityEngine.Object.FindObjectsOfType<TsvrcConfig>();

            if (existing.Length > 1)
            {
                Debug.LogError("[TsvrcCompiler] Multiple TsvrcConfig found in the scene \u2014 remove the duplicates and compile again.");
                return;
            }

            if (existing.Length == 1)
                return; // already present

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TsvrcConfigPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[TsvrcCompiler] TsvrcConfig prefab not found at '{TsvrcConfigPrefabPath}'.");
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(go, "Add TsvrcConfig");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[TsvrcCompiler] TsvrcConfig prefab added to scene.");
        }
    }
}
#endif
