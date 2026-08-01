#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // The scene's stable identity. See TsLinkedSceneConfig for why this is a GUID rather
        // than a path. Null when nothing has ever been linked.
        internal static string SceneGuid
        {
            get => LoadOrNull()?.SceneGuid;
            set
            {
                var config = LoadOrNull();
                if (config == null)
                {
                    config = ScriptableObject.CreateInstance<TsLinkedSceneConfig>();
                    Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? ".");
                    AssetDatabase.CreateAsset(config, ConfigPath);
                }
                config.SceneGuid = value;
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
        }

        // Resolved fresh from SceneGuid every time, so a rename or move made through the Project
        // window is reflected immediately with no separate resync step. Empty (not null) when a
        // GUID is on record but no longer resolves to any asset; see IsConfiguredButMissing.
        internal static string ScenePath
        {
            get
            {
                if (_hasOverride) return _overrideScenePath;
                string guid = SceneGuid;
                return string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
            }
            set
            {
                if (string.IsNullOrEmpty(value)) { SceneGuid = null; return; }
                string guid = AssetDatabase.AssetPathToGUID(value);
                if (string.IsNullOrEmpty(guid))
                {
                    // A scene just saved to a brand-new path isn't guaranteed to already be in
                    // AssetDatabase's GUID cache. Forcing a synchronous import here avoids
                    // silently failing to link a scene that genuinely exists on disk.
                    AssetDatabase.ImportAsset(value, ImportAssetOptions.ForceSynchronousImport);
                    guid = AssetDatabase.AssetPathToGUID(value);
                }
                SceneGuid = string.IsNullOrEmpty(guid) ? null : guid;
            }
        }

        internal static bool IsConfigured =>
            _hasOverride ? !string.IsNullOrEmpty(_overrideScenePath) : !string.IsNullOrEmpty(SceneGuid);

        // True once a scene has been linked but that exact scene isn't currently among the
        // loaded scenes, for any reason, including the asset having been deleted outright
        // (IsConfiguredButMissing is a strict subset of this). RunCore/ManualGenerate treat this
        // as a hard "do nothing": no other loaded scene is ever a legitimate substitute once one
        // has been explicitly chosen.
        internal static bool IsConfiguredButNotLoaded => IsConfigured && FindLoadedScene() == null;

        // True once a scene is linked but its GUID no longer resolves to any asset path at all:
        // the linked scene file (and its .meta) was deleted outright, not merely renamed or
        // moved. A strict subset of IsConfiguredButNotLoaded, used only to pick a more specific
        // UI message ("pick a new scene" vs. "open it").
        internal static bool IsConfiguredButMissing =>
            !_hasOverride && !string.IsNullOrEmpty(SceneGuid) && string.IsNullOrEmpty(ScenePath);

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
        //
        // Once scoped to a real linked scene, more than one match returns null rather than
        // silently picking Unity's arbitrary iteration-order winner, mirroring InstanceModule's
        // own "refuse to guess" pattern. TsGenerator.RunCore turns this into a loud diagnostic;
        // this method stays silent so its several call sites don't each have to log the same
        // thing. The legacy any-loaded-scene fallback below keeps its old, unscoped behavior.
        internal static T Find<T>() where T : UnityEngine.Object
        {
            if (!IsConfigured) return UnityEngine.Object.FindObjectOfType<T>(true);
            var scene = FindLoadedScene();
            if (scene == null) return null;
            T found = null;
            foreach (var root in scene.Value.GetRootGameObjects())
                foreach (var candidate in root.GetComponentsInChildren<T>(true))
                {
                    if (found != null) return null; // a second match is ambiguous, never guess
                    found = candidate;
                }
            return found;
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

        // Scene-scoped equivalent of UnityEngine.Object.FindObjectsOfType<T>(true), for modules
        // that need every match rather than just the first (TranslationModule's TMP target scan).
        // Same fallback as Find<T>()/FindType(): every loaded scene when nothing is configured,
        // empty when configured but not currently loaded.
        internal static List<T> FindAll<T>() where T : UnityEngine.Object
        {
            if (!IsConfigured) return new List<T>(UnityEngine.Object.FindObjectsOfType<T>(true));
            var scene = FindLoadedScene();
            var result = new List<T>();
            if (scene == null) return result;
            foreach (var root in scene.Value.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        // Runtime-Type twin of FindAll<T>(), for callers that only know the type reflectively
        // (ScaffoldModule's compiled root type). Same fallback/scoping behavior as FindType(Type).
        internal static List<Component> FindAllType(Type type)
        {
            if (!IsConfigured)
                return new List<Component>(UnityEngine.Object.FindObjectsOfType(type, true).Cast<Component>());
            var scene = FindLoadedScene();
            var result = new List<Component>();
            if (scene == null) return result;
            foreach (var root in scene.Value.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren(type, true));
            return result;
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
