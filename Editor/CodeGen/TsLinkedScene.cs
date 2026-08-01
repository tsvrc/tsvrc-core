#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Editor
{
    // The one scene TsGenerator is allowed to read scene-sourced config from: TsConfig,
    // [WirePool] scans, the compiled scaffold root. Before this existed, every lookup searched
    // "whatever scene happens to be loaded", which let a Play Mode test's temp scene, or simply
    // switching to look at an unrelated scene, silently regenerate the real Assets/TsGenerated
    // with an almost-empty result - a scene with no TsConfig compiling clean is, by design (see
    // TsModule.ApplySnapshotFallback), treated as the developer having genuinely removed
    // everything.
    //
    // Set from Tsvrc > Configure, persisted in a small per-project asset (TsLinkedSceneConfig).
    // Until a project sets one, every lookup falls back to the legacy "search whatever scene is
    // loaded" behavior, so an unconfigured project keeps working exactly as before.
    //
    // SetOverride/ClearOverride are a test-only seam, the same pattern TsPaths already uses:
    // EditMode's TempSceneScope points this at its own synthetic scene for the scope's lifetime,
    // so CodeGen's own tests keep resolving their synthetic TsConfig regardless of what a real
    // consuming project has configured.
    internal static class TsLinkedScene
    {
        // Derived from TsPaths.GeneratedFolder rather than hardcoded, so the same scratch-folder
        // redirect CodeGen tests already use also isolates this asset - a test exercising the
        // real ScenePath setter never writes into a consuming project's actual Assets/TsGenerated.
        private static string ConfigPath => $"{TsPaths.GeneratedFolder}/TsLinkedSceneConfig.asset";

        private static bool _hasOverride;
        private static string _overrideScenePath;

        internal static string ScenePath
        {
            get => _hasOverride ? _overrideScenePath : LoadOrNull()?.ScenePath;
            set
            {
                var config = LoadOrNull();
                if (config == null)
                {
                    config = ScriptableObject.CreateInstance<TsLinkedSceneConfig>();
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? ".");
                    AssetDatabase.CreateAsset(config, ConfigPath);
                }
                config.ScenePath = value;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
        }

        internal static bool IsConfigured => !string.IsNullOrEmpty(ScenePath);

        // True once a scene has been linked but that exact scene isn't currently among the
        // loaded scenes. RunCore treats this as a hard "do nothing" - no other loaded scene is
        // ever a legitimate substitute once one has been explicitly chosen.
        internal static bool IsConfiguredButNotLoaded => IsConfigured && FindLoadedScene() == null;

        internal static Scene? FindLoadedScene()
        {
            string path = ScenePath;
            if (string.IsNullOrEmpty(path)) return null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && scene.path == path) return scene;
            }
            return null;
        }

        // The scene PoolModule's ScanExternalRefs scans for [WirePool] fields: the linked scene
        // once configured (or null if it isn't currently loaded), otherwise the legacy "active
        // scene" behavior.
        internal static Scene? SceneToScan => IsConfigured ? FindLoadedScene() : (Scene?)SceneManager.GetActiveScene();

        // Scene-scoped equivalent of UnityEngine.Object.FindObjectOfType<T>(true). Falls back to
        // the legacy any-loaded-scene search when nothing is configured, including under a
        // test's override, which is deliberately an empty path (see SetOverride).
        internal static T Find<T>() where T : UnityEngine.Object
        {
            if (!IsConfigured) return UnityEngine.Object.FindObjectOfType<T>(true);
            var scene = FindLoadedScene();
            if (scene == null) return null;
            foreach (var root in scene.Value.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        internal static Component FindType(Type type)
        {
            if (!IsConfigured) return (Component)UnityEngine.Object.FindObjectOfType(type, true);
            var scene = FindLoadedScene();
            if (scene == null) return null;
            foreach (var root in scene.Value.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren(type, true);
                if (found != null) return found;
            }
            return null;
        }

        // Test-only seam - see the class doc comment. An empty scenePath (an unsaved scene's
        // path) makes IsConfigured false, so lookups fall back to the legacy any-loaded-scene
        // search scoped to whatever scene the caller actually set up.
        internal static void SetOverride(string scenePath)
        {
            _hasOverride = true;
            _overrideScenePath = scenePath;
        }

        internal static void ClearOverride()
        {
            _hasOverride = false;
            _overrideScenePath = null;
        }

        private static TsLinkedSceneConfig LoadOrNull() => AssetDatabase.LoadAssetAtPath<TsLinkedSceneConfig>(ConfigPath);
    }
}
#endif
