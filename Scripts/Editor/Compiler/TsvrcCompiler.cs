#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using Tsvrc.Core;
using UdonSharp;
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
            new UiUtilitiesModule(),
        };

        [MenuItem("Tsvrc/Tools/Force Compile")]
        public static void Compile() => Compile(refreshAssetDatabase: true);

        internal static void Compile(bool refreshAssetDatabase)
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
            if (refreshAssetDatabase)
                AssetDatabase.Refresh();
        }

        internal static void LogSuccess()
        {
            Debug.Log("[Tsvrc Compiler] Tsvrc has been successfully compiled. CompiledTsvrc has been generated and wired into the scene.");
        }

        // Instantiates TsvrcConfig into the active scene from the Tsvrc prefab.
        // Used by the Configure window's "Add TsvrcConfig to Scene" button.
        internal static TsvrcConfig AddTsvrcConfigToScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TsvrcConfigPrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[TsvrcCompiler] TsvrcConfig prefab not found at " + TsvrcConfigPrefabPath);
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.tag = "EditorOnly";
            go.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(go, "Add TsvrcConfig");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return go.GetComponent<TsvrcConfig>();
        }

        private static void CleanPrevious()
        {
            // Remove CompiledTsvrc GameObject from scene by name — avoids iterating all components.
            var existing = GameObject.Find("CompiledTsvrc");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);

            // Delete only the auto-generated files; never wipe the whole folder so that
            // user assets (TranslationConfig.asset, MolInstance.asset, …) are preserved.
            DeleteGeneratedAsset(GeneratedFilePath);
        }

        // Creates the UdonSharp program asset (.asset) for a given script if it does not already exist.
        // Returns true if the asset already exists or was successfully created; false if the script was not found.
        internal static bool EnsureUdonSharpProgramAsset(string scriptPath, string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                return true;

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (monoScript == null)
                return false;

            var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = monoScript;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            AssetDatabase.SaveAssets();
            return true;
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
                    var config = AddTsvrcConfigToScene();
                    if (config == null)
                        Debug.LogError("[TsvrcCompiler] No TsvrcConfig found in the scene. Open Tsvrc > Configure to set one up.");
                    return config;
            }
        }
    }
}
#endif
