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
        internal const string GeneratedFilePath = "Assets/CompiledTsvrc/CompiledTsvrc.cs";
        internal const string GeneratedAssetPath = "Assets/CompiledTsvrc/CompiledTsvrc.asset";
        internal const string TsvrcConfigPrefabPath = "Assets/Tsvrc/Prefabs/TsvrcConfig.prefab";
        internal const string InternalConfigPath = "Assets/Tsvrc/InternalConfig.asset";

        internal static InternalTsvrcConfig LoadInternalConfig()
            => AssetDatabase.LoadAssetAtPath<InternalTsvrcConfig>(InternalConfigPath);

        // Add new modules here to extend the compiler.
        internal static List<TsvrcModule> CreateModules() => new List<TsvrcModule>
        {
            new SingletonModule(),
            new ConstructModule(),
            new FactoryModule(),
        };

        [MenuItem("Tsvrc/Tools/Force Compile")]
        public static void Compile() => Compile(refreshAssetDatabase: true);

        // refreshAssetDatabase should be false when called from the VRChat build pipeline:
        // AssetDatabase.Refresh() mid-build can trigger a domain reload and corrupt the upload.
        // Returns false if compilation could not proceed (e.g. no TsvrcConfig in scene).
        internal static bool Compile(bool refreshAssetDatabase)
        {
            var config = RequireTsvrcConfig();
            if (config == null) return false;

            if (config.tag != "EditorOnly")
                Debug.LogWarning("[TsvrcCompiler] TsvrcConfig GameObject is not tagged 'EditorOnly'. It will be included in the VRChat build. Set the tag to 'EditorOnly' in the Inspector.");

            // During a build (refreshAssetDatabase: false) the scene already has the correct
            // wired CompiledTsvrc from the last normal compile. Cleaning would destroy that GO
            // and remove the .cs from the AssetDatabase without a Refresh to restore it, causing
            // UdonSharpBuildChecks to abort on the now-null script reference.
            if (refreshAssetDatabase)
                CleanPrevious();

            var modules = CreateModules();

            SourceScanner.ExcludeFolder = GeneratedFolder;
            SourceScanner.ClearSourceCache();
            foreach (var module in modules)
                module.Scan(config);

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string folderFull = ToAbsolutePath(projectRoot, GeneratedFolder);
            string fileFull = ToAbsolutePath(projectRoot, GeneratedFilePath);

            Directory.CreateDirectory(folderFull);

            string source = TsvrcCodeGen.Build(modules);
            File.WriteAllText(fileFull, source, Encoding.UTF8);

            if (refreshAssetDatabase)
            {
                // ScheduleWire only when Refresh follows: the domain reload it triggers is what
                // lets TsvrcWirer pick up the newly compiled CompiledTsvrc type. Without Refresh
                // (e.g. during a VRChat build) no reload occurs and wiring the scene is wrong.
                TsvrcWirer.ScheduleWire();
                AssetDatabase.Refresh();
            }

            return true;
        }

        internal static void LogSuccess()
        {
            Debug.Log("[Tsvrc Compiler] Tsvrc has been successfully compiled. CompiledTsvrc has been generated and wired into the scene.");
        }

        // Dry-run compile: scans source files and builds the generated output string, then
        // compares it to what is currently on disk. Returns true if a real Compile() call
        // would produce a different file.
        // Used by TsvrcWatcher to avoid triggering a domain reload when .cs files are saved
        // but no Tsvrc call sites were actually added or removed.
        internal static bool WouldChangeSource()
        {
            // Do not auto-create a TsvrcConfig during a dry run, read only.
            var all = Object.FindObjectsOfType<TsvrcConfig>(true);
            if (all.Length != 1) return false;

            var modules = CreateModules();
            SourceScanner.ExcludeFolder = GeneratedFolder;
            SourceScanner.ClearSourceCache();
            foreach (var module in modules)
                module.Scan(all[0]);

            string newSource = TsvrcCodeGen.Build(modules);

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fileFull = ToAbsolutePath(projectRoot, GeneratedFilePath);
            if (!File.Exists(fileFull)) return true;
            return File.ReadAllText(fileFull, Encoding.UTF8) != newSource;
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
            // GameObject.Find skips inactive objects, search root objects instead so a
            // deactivated CompiledTsvrc doesn't silently persist and duplicate after wire.
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "CompiledTsvrc")
                {
                    Undo.DestroyObjectImmediate(root);
                    break;
                }
            }

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
            AssetDatabase.SaveAssetIfDirty(programAsset);
            return true;
        }

        private static void DeleteGeneratedAsset(string assetPath)
        {
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)))
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static string ToAbsolutePath(string root, string assetPath)
            => Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));

        private static TsvrcConfig RequireTsvrcConfig()
        {
            var all = Object.FindObjectsOfType<TsvrcConfig>(true);

            switch (all.Length)
            {
                case > 1:
                    Debug.LogError("[TsvrcCompiler] Multiple TsvrcConfig found. Remove duplicates and recompile.");
                    return null;

                case 1:
                    return all[0];

                default:
                    // AddTsvrcConfigToScene already logs if the prefab is missing.
                    return AddTsvrcConfigToScene();
            }
        }
    }
}
#endif
