#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Tsvrc.Config;
using Tsvrc.Core;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Orchestrates all generator modules. Loads their config, writes generated files,
    // refreshes and recompiles as needed, wires scene references, then watches for changes to
    // rerun. Automatic triggers only proceed once HasBootstrapSignal() finds a reason this
    // project actually uses Tsvrc.
    internal static class TsGenerator
    {
        private const string PendingBootstrapKey = "Tsvrc.PendingBootstrap";

        internal static HashSet<string> WatchedPaths { get; private set; } = new HashSet<string>();

        // True while a deliberate bootstrap (a "Force Regenerate" or "Initialize Tsvrc" click) has
        // written files and is waiting for the recompile it triggered to settle. This is session
        // scoped by design: it only needs to survive the domain reload that follows a write, never
        // a full editor restart. Consumed and cleared by the very next AfterDomainReload() call.
        internal static bool IsBootstrapPending => SessionState.GetBool(PendingBootstrapKey, false);

        // Fired at the end of every Run() pass, regardless of which branch it exited through, so
        // editor windows can react to "something may have changed" instead of polling on OnFocus.
        internal static event Action StateChanged;

        // Every module logs warnings and errors with a "[ModuleName]", "[Tsvrc]" or
        // "[TsGenerator]" prefix, an established convention across all modules. Matched here to
        // distinguish Tsvrc's own diagnostics from unrelated log noise that might occur during
        // the same Run() pass.
        private static readonly Regex TsLogPrefix = new Regex(@"^\[(Tsvrc|TsGenerator|\w+Module)\]", RegexOptions.Compiled);

        // Warnings/errors logged by the most recent Run() pass, so TsWindow can point at them
        // instead of a user only finding out by happening to have the Console open. Complements
        // (does not replace) LastFieldNameCollisions, which gives that one specific, very common
        // case its own actionable message.
        internal static IReadOnlyList<string> LastRunWarnings { get; private set; } = Array.Empty<string>();

        private static List<TsModule> _activeModules;
        private static HashSet<string> _watchedComponentTypeNames = new HashSet<string>(StringComparer.Ordinal);
        private static bool _rerunPending;

        // True for the entire duration of an automated test run (EditMode or PlayMode), computed
        // once from the process's real command line. This is the only signal available at the
        // one moment TsDomainReloadHandler's static constructor fires: right after the very first
        // domain reload, before Unity Test Framework has discovered or started running anything,
        // so no test-side SetUpFixture has had a chance to run yet either. See
        // AutomaticTriggersSuppressed for the second, complementary signal that covers everything
        // after that moment.
        private static readonly bool IsAutomatedTestProcess = ComputeIsAutomatedTestProcess(Environment.GetCommandLineArgs());

        // Ref-counted rather than a bool so nested SuppressAutomaticTriggers() scopes (for
        // example a [SetUpFixture] wrapping a whole test assembly's run, with an individual
        // test's own harness also taking a scope inside it) compose correctly: suppression only
        // lifts once every scope that requested it has been disposed.
        private static int _suppressionDepth;

        // The one flag every automatic (non-explicit) entry point below must check before
        // scheduling or running a real pass: the domain-reload trigger, the hierarchyChanged
        // watch armed by a completed Run(), and the asset-watcher trigger (via ScheduleRerun()).
        // Deliberate, explicit calls a test makes directly - Run(), AfterDomainReload() - are
        // never gated by this: gating those would break every CodeGen test that drives TsGenerator
        // on purpose to test it. Only the reactive paths that would otherwise fire against
        // whatever scene Unity Test Framework happens to have open are suppressed.
        internal static bool AutomaticTriggersSuppressed => _suppressionDepth > 0 || IsAutomatedTestProcess;

        // Held by a [SetUpFixture] in each test assembly (Tsvrc.Tests.EditMode,
        // Tsvrc.Tests.PlayMode) for the whole run, so no reactive trigger can regenerate
        // TsGenerated against a test's temp/synthetic scene and corrupt a consuming project's
        // real generated files. Safe to nest.
        internal static IDisposable SuppressAutomaticTriggers() => new SuppressionScope();

        private sealed class SuppressionScope : IDisposable
        {
            private bool _disposed;
            internal SuppressionScope() => _suppressionDepth++;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _suppressionDepth--;
            }
        }

        // Pure and testable on its own: TsDomainReloadHandler used to duplicate this exact
        // parsing logic locally. One source of truth here instead.
        internal static bool ComputeIsAutomatedTestProcess(string[] commandLineArgs)
        {
            foreach (var arg in commandLineArgs)
                if (string.Equals(arg, "-runTests", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
        // Suppresses the rerun during the Wire pass itself, since Wire() assigns SerializedObject
        // properties, which fires OnPostprocessModifications.
        private static bool _isWiring;
        // Suppresses the rerun for one editor frame after Wire() returns, because Unity commits
        // serialized changes asynchronously and the modification event can arrive after
        // _isWiring is already cleared.
        private static bool _justFinishedWiring;

        [MenuItem("Tsvrc/Force Regenerate", priority = 21)]
        public static void ManualGenerate()
        {
            Run(allowBootstrap: true);
            Debug.Log("[Tsvrc] Regenerated.");
        }

        // Greys the menu item out during play mode instead of letting the click silently no-op.
        // Run() itself already bails on isPlayingOrWillChangePlaymode, so this is purely a UX
        // improvement, not a correctness guard.
        [MenuItem("Tsvrc/Force Regenerate", true)]
        private static bool ValidateManualGenerate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        // The automatic post-compile trigger, called by TsDomainReloadHandler. Consumes the
        // pending bootstrap flag set by a prior Run(allowBootstrap: true) that had to stop for a
        // recompile, carrying it through as allowBootstrap: true so one "Force Regenerate" click
        // completes the whole scaffold sequence without a second click.
        internal static void AfterDomainReload(bool skipRefresh = false)
        {
            bool pending = SessionState.GetBool(PendingBootstrapKey, false);
            if (pending) SessionState.SetBool(PendingBootstrapKey, false);
            Run(skipRefresh, allowBootstrap: pending);
        }

        // allowBootstrap false, the default used by every automatic trigger, waits for
        // HasBootstrapSignal() via WaitForBootstrapSignal instead of creating the scaffold
        // outright. ManualGenerate() passes true, since a deliberate click is itself the
        // bootstrap signal.
        internal static void Run(bool skipRefresh = false, bool allowBootstrap = false)
        {
            var collectedWarnings = new List<string>();
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if ((type == LogType.Warning || type == LogType.Error) && TsLogPrefix.IsMatch(condition))
                    collectedWarnings.Add(condition);
            }

            Application.logMessageReceived += OnLog;
            try
            {
                RunCore(skipRefresh, allowBootstrap);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                LastRunWarnings = collectedWarnings;
                StateChanged?.Invoke();
            }
        }

        private static void RunCore(bool skipRefresh, bool allowBootstrap)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // Once a project has linked a scene (Tsvrc > Configure), no other loaded scene - a
            // test's temp scene, or simply having something else open - is ever a legitimate
            // source of scene config. See TsLinkedScene's own doc comment for why this matters
            // more than just gating automatic triggers: an explicit Run()/ManualGenerate() call
            // must be just as safe, not only the reactive paths.
            if (TsLinkedScene.IsConfiguredButNotLoaded) return;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            Undo.postprocessModifications -= OnPostprocessModifications;
            _activeModules = null;

            // Rebuilt fresh every pass and shared by every module's IsTsvrcBehaviourType lookups
            // within this pass. Never cached across passes, since a script may have just been
            // added, renamed, or removed since the last one.
            ScriptIndex.Rebuild();

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

            DetectAndExcludeFieldNameCollisions(modules);

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules)
                foreach (var path in module.WatchedAssets())
                    paths.Add(path);
            // Generated files are always watched so deleting any of them triggers a rerun.
            foreach (var module in modules)
                if (module.FileName != null)
                    paths.Add($"{TsPaths.GeneratedFolder}/{module.FileName}");
            WatchedPaths = paths;

            if (WriteModules(modules))
            {
                // Stop here even with skipRefresh, which TsBuildCompile passes at build time: the
                // compiled type on disk is now stale relative to what was just written, so wiring
                // against it would target the wrong field set. The recompile this triggers calls
                // AfterDomainReload() again with the now current type, and that pass is what
                // actually wires.
                if (allowBootstrap) SessionState.SetBool(PendingBootstrapKey, true);
                if (!skipRefresh) AssetDatabase.Refresh();
                return;
            }

            bool stableChanged = false;
            foreach (var module in modules)
                stableChanged |= module.AfterFilesStable();

            // stableChanged alone (for example, a program asset was recreated) is enough to need
            // a Refresh even if no .cs file changed, since UdonSharp won't re-link the backing
            // UdonBehaviour until it processes the new asset.
            bool filesWritten = WriteModules(modules);
            if (filesWritten || stableChanged)
            {
                if (allowBootstrap) SessionState.SetBool(PendingBootstrapKey, true);
                if (!skipRefresh) AssetDatabase.Refresh();
                return;
            }

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
                nameof(TsConfig),
            };
            foreach (var module in modules)
                foreach (var name in module.WatchedComponentTypeNames())
                    watchedTypes.Add(name);
            _watchedComponentTypeNames = watchedTypes;

            _activeModules = modules;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.postprocessModifications += OnPostprocessModifications;
        }

        // Checked once per hierarchyChanged event while Run() is gated waiting for a bootstrap
        // reason. Stops watching and runs for real the moment one appears.
        private static void WaitForBootstrapSignal()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AutomaticTriggersSuppressed) return;
            if (!HasBootstrapSignal()) return;
            EditorApplication.hierarchyChanged -= WaitForBootstrapSignal;
            Run(allowBootstrap: true);
        }

        // Types marked [TsCodegenIgnore], such as a test double Instance subclass, are excluded
        // from the Instance scan below. See TsCodegenIgnoreAttribute's own doc comment for why.
        //
        // Internal rather than private so TsBuildCompile can ask the same question at build
        // time, deciding whether skipping bootstrap silently is safe or the user should be
        // warned.
        internal static bool HasBootstrapSignal()
        {
            if (TsLinkedScene.Find<TsConfig>() != null) return true;

            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType != null && TsLinkedScene.FindType(compiledType) != null) return true;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var type in types)
                    if (type != typeof(Instance) && !type.IsAbstract && typeof(Instance).IsAssignableFrom(type)
                        && type.GetCustomAttribute<TsCodegenIgnoreAttribute>() == null)
                        return true;
            }
            return false;
        }

        internal static void ScheduleRerun()
        {
            if (AutomaticTriggersSuppressed) return;
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
            // tool actually changed TsGenerated or TsConfig properties.
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

        // Module order within a Run() pass does not affect correctness: each module reads from
        // independent scene and asset sources and writes to independent serialized fields.
        // Also used by TsWindow to build its tab list.
        internal static List<TsModule> CreateModules() => new List<TsModule>
        {
            new LogModule(),
            new MemoryModule(),
            new PoolModule(),
            new TranslationModule(),
            new InstanceModule(),
            new SingletonModule(),
            new ConstructModule(),
            new FactoryModule(),
            new ScaffoldModule(),
        };

        // Any field name exposed by more than one module is stripped from every module that
        // declared it, so a collision never silently produces two same named fields on
        // TsGenerated. Only SingletonModule currently overrides ExposedFieldNames() and
        // ExcludeFieldNames() among the real modules, so this path is otherwise only
        // exercisable with synthetic test modules.
        internal static void DetectAndExcludeFieldNameCollisions(List<TsModule> modules)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in modules)
                foreach (var name in module.ExposedFieldNames())
                    if (!seen.Add(name))
                        duplicates.Add(name);
            if (duplicates.Count > 0)
                foreach (var module in modules)
                    module.ExcludeFieldNames(duplicates);

            // Recorded even when empty, so a collision fixed by the user is reflected on the very
            // next Run() rather than lingering in TsWindow's warning box.
            LastFieldNameCollisions = duplicates.OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        // Field names dropped by the most recent Run() because more than one module tried to
        // expose the same name, for example a Singleton and a Construct both deriving
        // "GameManager". Surfaced in TsWindow as a warning instead of only the console
        // Debug.LogError each affected module already logs in its ExcludeFieldNames() override.
        internal static IReadOnlyList<string> LastFieldNameCollisions { get; private set; } = Array.Empty<string>();

        private static bool WriteModules(List<TsModule> modules)
        {
            bool written = false;
            foreach (var module in modules)
            {
                if (module.FileName == null) continue;
                written |= WriteIfChanged($"{TsPaths.GeneratedFolder}/{module.FileName}", module.GenerateCode());
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
