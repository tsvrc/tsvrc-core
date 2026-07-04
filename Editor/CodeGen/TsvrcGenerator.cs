#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Tsvrc.Config;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Orchestrates all generator modules. A single Run() pass:
    //   0. Automatic triggers only proceed if HasBootstrapSignal() finds a reason this project
    //      actually uses Tsvrc; otherwise nothing is written or created (see Run()).
    //   1. Calls LoadConfig() on every module (reads scene and asset state).
    //   2. Resolves cross-module field name conflicts via ExposedFieldNames/ExcludeFieldNames.
    //   3. Writes generated .cs files via WriteModules(); if any file changed, triggers
    //      AssetDatabase.Refresh() and returns — Unity must recompile before wiring.
    //   4. Calls AfterFilesStable() (creates scene objects that depend on compiled types).
    //      If that triggers another write, refreshes again.
    //   5. Calls Wire() on every module to assign scene references into serialized fields.
    //   6. Subscribes to hierarchy/undo events to detect incremental changes and rerun.
    internal static class TsvrcGenerator
    {
        private const string GeneratedFolder = "Assets/TsvrcGenerated";

        internal static HashSet<string> WatchedPaths { get; private set; } = new HashSet<string>();

        private static List<TsvrcModule> _activeModules;
        private static HashSet<string> _watchedComponentTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private static bool _rerunPending;
        // Wire() assigns SerializedObject properties, which fires OnPostprocessModifications.
        // _isWiring suppresses the rerun during the Wire pass itself.
        // _justFinishedWiring suppresses it for one editor frame after Wire() returns,
        // because Unity commits serialized changes asynchronously and the modification
        // event can arrive after _isWiring is already cleared.
        private static bool _isWiring;
        private static bool _justFinishedWiring;

        [MenuItem("Tsvrc/Force Regenerate")]
        public static void ManualGenerate()
        {
            Run(allowBootstrap: true);
            Debug.Log("[Tsvrc] Regenerated.");
        }

        internal static void AfterDomainReload(bool skipRefresh = false) => Run(skipRefresh);

        // allowBootstrap: false (the default, used by every automatic trigger - domain reload,
        // asset watcher, hierarchy/undo watcher) means Run() will not create the scaffold from
        // nothing. It only proceeds if HasBootstrapSignal() finds a reason to believe this
        // project actually uses Tsvrc; otherwise it waits for one via WaitForBootstrapSignal.
        // ManualGenerate() (the "Force Regenerate" menu item) passes true, since a deliberate
        // click is itself the bootstrap signal.
        internal static void Run(bool skipRefresh = false, bool allowBootstrap = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.postprocessModifications -= OnPostprocessModifications;
            _activeModules = null;

            if (!allowBootstrap && !HasBootstrapSignal())
            {
                EditorApplication.hierarchyChanged -= WaitForBootstrapSignal;
                EditorApplication.hierarchyChanged += WaitForBootstrapSignal;
                return;
            }
            EditorApplication.hierarchyChanged -= WaitForBootstrapSignal;

            var modules = CreateModules();

            foreach (var module in modules)
                module.LoadConfig();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in modules)
                foreach (var name in module.ExposedFieldNames())
                    if (!seen.Add(name))
                        duplicates.Add(name);
            if (duplicates.Count > 0)
                foreach (var module in modules)
                    module.ExcludeFieldNames(duplicates);

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules)
                foreach (var path in module.WatchedAssets())
                    paths.Add(path);
            // Generated files are always watched so deleting any of them triggers a rerun.
            foreach (var module in modules)
                if (module.FileName != null)
                    paths.Add($"{GeneratedFolder}/{module.FileName}");
            WatchedPaths = paths;

            if (WriteModules(modules) && !skipRefresh) { AssetDatabase.Refresh(); return; }

            bool stableChanged = false;
            foreach (var module in modules)
                stableChanged |= module.AfterFilesStable();

            // stableChanged alone (e.g. a program asset was recreated) is enough to need a
            // Refresh even if no .cs file changed — UdonSharp won't re-link the backing
            // UdonBehaviour until it processes the new asset.
            bool filesWritten = WriteModules(modules);
            if ((filesWritten || stableChanged) && !skipRefresh) { AssetDatabase.Refresh(); return; }

            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType != null && UnityEngine.Object.FindObjectOfType(compiledType, true) != null)
            {
                _isWiring = true;
                try { foreach (var module in modules) module.Wire(); }
                finally
                {
                    _isWiring = false;
                    _justFinishedWiring = true;
                    EditorApplication.delayCall += () => _justFinishedWiring = false;
                }
            }

            var watchedTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                ScaffoldModule.CompiledClassName,
                nameof(TsvrcConfig),
            };
            foreach (var module in modules)
                foreach (var name in module.WatchedComponentTypeNames())
                    watchedTypes.Add(name);
            _watchedComponentTypeNames = watchedTypes;

            _activeModules = modules;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.postprocessModifications += OnPostprocessModifications;
        }

        // Checked once per hierarchyChanged event while gated (see Run()). Stops watching and
        // runs for real the moment a reason to bootstrap appears.
        private static void WaitForBootstrapSignal()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!HasBootstrapSignal()) return;
            EditorApplication.hierarchyChanged -= WaitForBootstrapSignal;
            Run(allowBootstrap: true);
        }

        // True if there's a concrete reason to believe this project uses Tsvrc: an existing
        // TsvrcConfig, an existing scaffold instance in the scene (already bootstrapped, this
        // is just maintenance), or a user-authored TsvrcInstance subclass anywhere in the
        // project (declared before ever placing it in a scene).
        private static bool HasBootstrapSignal()
        {
            if (UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true) != null) return true;

            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType != null && UnityEngine.Object.FindObjectOfType(compiledType, true) != null) return true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var type in types)
                    if (type != typeof(TsvrcInstance) && !type.IsAbstract && typeof(TsvrcInstance).IsAssignableFrom(type))
                        return true;
            }
            return false;
        }

        internal static void ScheduleRerun()
        {
            if (_rerunPending) return;
            _rerunPending = true;
            EditorApplication.delayCall += RunScheduled;
        }

        private static void RunScheduled()
        {
            _rerunPending = false;
            Run();
        }

        private static void OnHierarchyChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (_activeModules == null) return;
            bool changed = false;
            foreach (var module in _activeModules)
                changed |= module.OnSceneHierarchyChanged();
            if (changed)
                ScheduleRerun();
        }

        private static UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            // Ignore modifications triggered by our own Wire() pass (both during and for one
            // frame after) and play-mode transitions. Only re-run when the user or an external
            // tool actually changed TsvrcGenerated or TsvrcConfig properties.
            if (_isWiring || _justFinishedWiring || EditorApplication.isPlayingOrWillChangePlaymode || _activeModules == null)
                return modifications;
            foreach (var mod in modifications)
            {
                var target = mod.currentValue?.target;
                if (target == null) continue;
                if (_watchedComponentTypeNames.Contains(target.GetType().Name))
                {
                    ScheduleRerun();
                    return modifications;
                }
            }
            return modifications;
        }

        // Module order within a Run() pass does not affect correctness — each module reads
        // from independent scene/asset sources and writes to independent serialized fields.
        // Also used by TsvrcWindow to build its tab list.
        internal static List<TsvrcModule> CreateModules() => new List<TsvrcModule>
        {
            new MemoryModule(),
            new PoolModule(),
            new TranslationModule(),
            new InstanceModule(),
            new SingletonModule(),
            new ConstructModule(),
            new FactoryModule(),
            new ScaffoldModule(),
        };

        private static bool WriteModules(List<TsvrcModule> modules)
        {
            bool written = false;
            foreach (var module in modules)
            {
                if (module.FileName == null) continue;
                written |= WriteIfChanged($"{GeneratedFolder}/{module.FileName}", module.GenerateCode());
            }
            return written;
        }

        // Content comparison before writing avoids touching the file when output is identical,
        // which would otherwise trigger an AssetDatabase.Refresh() and a full reimport cycle
        // on every Run() even when nothing changed.
        private static bool WriteIfChanged(string assetPath, string content)
        {
            string fullPath = ToFullPath(assetPath);
            if (File.Exists(fullPath) && File.ReadAllText(fullPath, Encoding.UTF8) == content)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content, Encoding.UTF8);
            return true;
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
#endif
