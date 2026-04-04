#if UNITY_EDITOR
using System.Collections.Generic;
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
        internal const string GeneratedFolder = "Assets/CompiledTsvrc";
        private const string GeneratedFilePath = "Assets/CompiledTsvrc/CompiledTsvrc.cs";
        internal const string TsvrcConfigPrefabPath = "Assets/Tsvrc/Prefabs/TsvrcConfig.prefab";
        internal const string InternalConfigPath = "Assets/Tsvrc/InternalConfig.asset";

        internal static InternalTsvrcConfig LoadInternalConfig()
            => AssetDatabase.LoadAssetAtPath<InternalTsvrcConfig>(InternalConfigPath);

        // Add new modules here to extend the compiler.
        internal static List<TsvrcModule> CreateModules() => new List<TsvrcModule>
        {
            new MemoryModule(),
            new SingletonModule(),
            new PoolModule(),
            new ConstructModule(),
            new InstanceModule(),
            new FactoryModule(),
            new TranslationModule(),
        };

        [MenuItem("Tsvrc/Compile")]
        public static void Compile()
        {
            var config = RequireTsvrcConfig();
            if (config == null) return;

            if (config.tag != "EditorOnly")
                Debug.LogWarning("[TsvrcCompiler] TsvrcConfig GameObject is not tagged 'EditorOnly'. It will be included in the VRChat build. Set the tag to 'EditorOnly' in the Inspector.");

            CleanPrevious();

            var modules = CreateModules();

            SourceScanner.ExcludeFolder = GeneratedFolder;
            foreach (var module in modules)
                module.Scan(config);

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string folderFull = ToAbsolutePath(projectRoot, GeneratedFolder);
            string fileFull = ToAbsolutePath(projectRoot, GeneratedFilePath);

            Directory.CreateDirectory(folderFull);

            string source = TsvrcCodeGen.Build(modules);
            File.WriteAllText(fileFull, source, Encoding.UTF8);

            TsvrcWirer.ScheduleWire();
            AssetDatabase.Refresh();
        }

        internal static void LogSuccess()
        {
            Debug.Log("[Tsvrc Compiler] Tsvrc has been successfully compiled. CompiledTsvrc has been generated and wired into the scene.");
        }

        private static void CleanPrevious()
        {
            // Remove CompiledTsvrc GameObject from scene
            var existing = Object.FindObjectsOfType<Component>();
            foreach (var c in existing)
            {
                if (c != null && c.GetType().FullName == "Tsvrc.Core.Compiled.CompiledTsvrc")
                {
                    Undo.DestroyObjectImmediate(c.gameObject);
                    break;
                }
            }

            // Delete only the auto-generated files; never wipe the whole folder so that
            // user assets (TranslationConfig.asset, MolInstance.asset, …) are preserved.
            DeleteGeneratedAsset(GeneratedFilePath);
            DeleteGeneratedAsset(GeneratedFolder + "/translations.txt");
        }

        private static void DeleteGeneratedAsset(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static string ToAbsolutePath(string root, string assetPath)
            => Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));

        private static TsvrcConfig RequireTsvrcConfig()
        {
            var all = Object.FindObjectsOfType<TsvrcConfig>();

            switch (all.Length)
            {
                case > 1:
                    Debug.LogError("[TsvrcCompiler] Multiple TsvrcConfig found. Remove duplicates and recompile.");
                    return null;

                case 1:
                    return all[0];

                default:
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TsvrcConfigPrefabPath);
                    if (prefab == null)
                    {
                        Debug.LogError("[TsvrcCompiler] No TsvrcConfig found in the scene. Open Tsvrc > Configure to set one up.");
                        return null;
                    }

                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.tag = "EditorOnly";
                    go.transform.SetSiblingIndex(0);
                    Undo.RegisterCreatedObjectUndo(go, "Add TsvrcConfig");
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

                    return go.GetComponent<TsvrcConfig>();
            }
        }
    }
}
#endif
