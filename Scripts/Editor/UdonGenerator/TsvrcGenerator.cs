#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    internal static class TsvrcGenerator
    {
        private const string GeneratedFolder = "Assets/TsvrcGenerated";

        internal static HashSet<string> WatchedPaths { get; private set; } = new HashSet<string>();

        private static List<TsvrcModule> _activeModules;
        private static bool _rerunPending;
        private static bool _isWiring;

        [MenuItem("Tsvrc/Generate")]
        public static void ManualGenerate()
        {
            Debug.Log("[TsvrcGenerator] === Manual Generate ===");
            Run();
        }

        internal static void AfterDomainReload(bool skipRefresh = false) => Run(skipRefresh);

        internal static void Run(bool skipRefresh = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.postprocessModifications -= OnPostprocessModifications;
            _activeModules = null;

            var modules = CreateModules();

            foreach (var module in modules)
                module.LoadConfig();

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules)
                foreach (var path in module.WatchedAssets())
                    paths.Add(path);
            WatchedPaths = paths;

            bool anyWritten = false;
            foreach (var module in modules)
                anyWritten |= WriteIfChanged($"{GeneratedFolder}/{module.FileName}", module.GenerateCode());

            if (anyWritten && !skipRefresh)
            {
                AssetDatabase.Refresh();
                return;
            }

            bool stableChanged = false;
            foreach (var module in modules)
                stableChanged |= module.AfterFilesStable();

            if (stableChanged)
            {
                bool stableWritten = false;
                foreach (var module in modules)
                    stableWritten |= WriteIfChanged($"{GeneratedFolder}/{module.FileName}", module.GenerateCode());
                if (stableWritten && !skipRefresh)
                {
                    AssetDatabase.Refresh();
                    return;
                }
            }

            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType != null)
            {
                var component = (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
                if (component != null)
                {
                    var scene = component.gameObject.scene;
                    _isWiring = true;
                    try { foreach (var module in modules) module.Wire(scene); }
                    finally { _isWiring = false; }
                }
            }

            _activeModules = modules;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.postprocessModifications += OnPostprocessModifications;
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
            if (_isWiring || EditorApplication.isPlayingOrWillChangePlaymode || _activeModules == null)
                return modifications;
            foreach (var mod in modifications)
            {
                var target = mod.currentValue?.target;
                if (target == null) continue;
                var typeName = target.GetType().Name;
                if (typeName == ScaffoldModule.CompiledClassName ||
                    typeName == ScaffoldModule.PoolClassName ||
                    typeName == ScaffoldModule.TranslationClassName)
                {
                    ScheduleRerun();
                    return modifications;
                }
            }
            return modifications;
        }

        private static List<TsvrcModule> CreateModules() => new List<TsvrcModule>
        {
            new PoolModule(),
            new TranslationModule(),
            new ScaffoldModule(),
        };

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
