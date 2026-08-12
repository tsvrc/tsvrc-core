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

        // Whether this pass represents genuine developer opportunity to have referenced a
        // tree-shaking-eligible entry: true for a real recompile or an explicit Force
        // Regenerate/Initialize click, false for a pass caused only by a reactive trigger (asset
        // watcher, hierarchy-changed watch, property-modification watch), since those can fire for
        // reasons unrelated to any given entry. Read by TsModule.ApplyTreeShaking.
        internal static bool CurrentPassCountsForGracePeriod { get; private set; } = true;

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
            // RunCore's own IsConfiguredButNotLoaded guard already makes Run() a real no-op here,
            // but without this check "Regenerated." would still log unconditionally below,
            // falsely implying it acted on the scene that's actually open. ValidateManualGenerate
            // already greys the menu item out for this same reason; this covers the Configure
            // window's button, which calls this directly rather than going through the menu.
            if (TsLinkedScene.IsConfiguredButNotLoaded)
            {
                Debug.LogWarning($"[Tsvrc] Linked scene '{TsLinkedScene.ScenePath}' is not open - nothing to regenerate. Open it first.");
                return;
            }
            // A deliberate click is itself real developer opportunity, exactly like a recompile.
            Run(allowBootstrap: true, countsForGracePeriod: true);
            Debug.Log("[Tsvrc] Regenerated.");
        }

        // Greys the menu item out during play mode or while the linked scene isn't open, instead
        // of letting the click silently no-op (play mode) or misleadingly log success against the
        // wrong scene (ManualGenerate's own IsConfiguredButNotLoaded check otherwise catches this
        // too, but graying the menu item out is friendlier than letting the click happen at all).
        [MenuItem("Tsvrc/Force Regenerate", true)]
        private static bool ValidateManualGenerate() => !EditorApplication.isPlayingOrWillChangePlaymode && !TsLinkedScene.IsConfiguredButNotLoaded;

        // The automatic post-compile trigger, called by TsDomainReloadHandler. Consumes the
        // pending bootstrap flag set by a prior Run(allowBootstrap: true) that had to stop for a
        // recompile, carrying it through as allowBootstrap: true so one "Force Regenerate" click
        // completes the whole scaffold sequence without a second click.
        internal static void AfterDomainReload(bool skipRefresh = false)
        {
            bool pending = SessionState.GetBool(PendingBootstrapKey, false);
            if (pending) SessionState.SetBool(PendingBootstrapKey, false);
            // A real recompile is unambiguous developer opportunity, so this always counts.
            Run(skipRefresh, allowBootstrap: pending, countsForGracePeriod: true);
        }

        // allowBootstrap false, the default used by every automatic trigger, waits for
        // HasBootstrapSignal() via WaitForBootstrapSignal instead of creating the scaffold
        // outright. ManualGenerate() passes true, since a deliberate click is itself the
        // bootstrap signal.
        internal static void Run(bool skipRefresh = false, bool allowBootstrap = false, bool countsForGracePeriod = true)
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
                RunCore(skipRefresh, allowBootstrap, countsForGracePeriod);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
                LastRunWarnings = collectedWarnings;
                StateChanged?.Invoke();
            }
        }

        // Bounds the stableChanged-only self-retry below: real modules converge in one extra
        // pass (AfterFilesStable() becomes a no-op once whatever it created/reparented already
        // exists), so this only ever fires if some module's AfterFilesStable() isn't idempotent.
        private const int MaxStableSettlePasses = 3;

        private static void RunCore(bool skipRefresh, bool allowBootstrap, bool countsForGracePeriod, int stableSettleDepth = 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            CurrentPassCountsForGracePeriod = countsForGracePeriod;
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

            // Checked once here, not once per module, even though Global/Pool/Factory each
            // independently read TsBuiltinConfig.
            if (TsModule.IsBuiltinConfigMissing())
                Debug.LogWarning($"[Tsvrc] Builtin config asset is missing at '{TsModule.BuiltinConfigPath}' - " +
                    "library-provided globals/pool prefabs/factories will not be included until it's restored.");

            // TsLinkedScene.Find<TsConfig>() itself now silently returns null on an ambiguous
            // match; this is the one place that turns that into a specific diagnostic naming
            // every duplicate's Hierarchy path, mirroring InstanceModule's own "multiple
            // subclasses found" pattern instead of leaving it a silent, arbitrary pick.
            var configWarning = DetermineAmbiguousConfigWarning(TsLinkedScene.FindAll<TsConfig>());
            if (configWarning != null)
                Debug.LogError(configWarning);

            foreach (var module in modules)
                module.LoadConfig();

            // Collected after LoadConfig() so each module's tree-shaking decisions for this pass
            // are final. Kept as two separate lists, not merged, so TsWindow can show "actually
            // gone" apart from "kept for now."
            var treeShakingExclusions = new List<string>();
            var treeShakingGraceIncluded = new List<string>();
            foreach (var module in modules)
            {
                treeShakingExclusions.AddRange(module.LastTreeShakingExclusions);
                treeShakingGraceIncluded.AddRange(module.LastTreeShakingGraceIncluded);
            }
            LastTreeShakingExclusions = treeShakingExclusions;
            LastTreeShakingGraceIncluded = treeShakingGraceIncluded;

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

            // A module that stops producing a file it used to (renamed or removed) leaves that
            // file behind forever otherwise, since WatchedPaths above only tracks each module's
            // current FileName. A leftover file redeclaring the same partial-class members as the
            // current one is a guaranteed compile break, not just clutter.
            bool orphansDeleted = DeleteOrphanedGeneratedFiles(modules);

            if (WriteModules(modules) || orphansDeleted)
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

            bool filesWritten = WriteModules(modules);
            if (filesWritten)
            {
                if (allowBootstrap) SessionState.SetBool(PendingBootstrapKey, true);
                if (!skipRefresh) AssetDatabase.Refresh();
                return;
            }

            if (stableChanged)
            {
                // No .cs content changed, so no recompile is coming and AfterDomainReload() will
                // never run to clear a pending flag set here. Refresh() still lets UdonSharp
                // reprocess whatever AfterFilesStable() created; settling then continues
                // synchronously in this same call rather than via EditorApplication.delayCall, so
                // CurrentPassCountsForGracePeriod can't be read by unrelated work running in
                // between (for example a test calling a module's LoadConfig() directly).
                if (!skipRefresh) AssetDatabase.Refresh();
                // Silence here is deliberate: this is a bound against runaway recursion in case
                // some module's AfterFilesStable() never converges, not a user-facing failure.
                // The next real trigger retries fresh regardless.
                if (stableSettleDepth < MaxStableSettlePasses)
                    RunCore(skipRefresh, allowBootstrap, countsForGracePeriod, stableSettleDepth + 1);
                return;
            }

            // Reached only when nothing needed writing or stabilizing. Self-clearing the pending
            // flag here, not only in AfterDomainReload(), means any pass that reaches settlement
            // resolves it, regardless of what triggered the pass.
            if (SessionState.GetBool(PendingBootstrapKey, false))
                SessionState.SetBool(PendingBootstrapKey, false);

            // Scoped to the linked scene: an unrelated compiled-type instance in an additively-
            // loaded scene must never gate Wire() open or shut for the scene actually being edited.
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType != null && TsLinkedScene.FindType(compiledType) != null)
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
            Run(allowBootstrap: true, countsForGracePeriod: true);
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
            // Every reactive trigger funnels through here regardless of which module's change
            // caused it, so none of them count toward any module's grace period.
            Run(countsForGracePeriod: false);
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
            new GlobalModule(),
            new ConstructModule(),
            new FactoryModule(),
            new ScaffoldModule(),
        };

        // Pure so it's directly unit-testable without a real scene. Returns null when there's
        // nothing to warn about (0 or 1 TsConfig found).
        internal static string DetermineAmbiguousConfigWarning(List<TsConfig> found)
        {
            if (found == null || found.Count <= 1) return null;
            var paths = found.Select(c => ScaffoldModule.GameObjectPath(c.gameObject));
            return $"[Tsvrc] Multiple TsConfig components found in the linked scene ({string.Join(", ", paths)}). " +
                "Exactly one is required; leaving the current wiring untouched until the duplicate is removed.";
        }

        // Any field name exposed by more than one module is stripped from every module that
        // declared it, so a collision never silently produces two same named fields on
        // TsGenerated. Only GlobalModule currently overrides ExposedFieldNames() and
        // ExcludeFieldNames() among the real modules, so this path is otherwise only
        // exercisable with synthetic test modules.
        internal static void DetectAndExcludeFieldNameCollisions(List<TsModule> modules)
        {
            // name -> every module that would declare it this pass.
            var exposers = new Dictionary<string, List<TsModule>>(StringComparer.Ordinal);
            foreach (var module in modules)
                foreach (var name in module.ExposedFieldNames())
                {
                    if (!exposers.TryGetValue(name, out var list))
                        exposers[name] = list = new List<TsModule>();
                    list.Add(module);
                }

            // Per-module set of names to strip. A name exposed by more than one module goes to the
            // highest FieldNamePrecedence; the rest drop it. An exact tie at the top is a genuine
            // collision and strips the name from every exposer.
            var toExclude = new Dictionary<TsModule, HashSet<string>>();
            var genuineCollisions = new List<string>();

            foreach (var pair in exposers)
            {
                var list = pair.Value;
                if (list.Count <= 1) continue;

                int maxPrec = list.Max(m => m.FieldNamePrecedence);
                var winners = list.Where(m => m.FieldNamePrecedence == maxPrec).ToList();

                if (winners.Count == 1)
                {
                    // Precedence-resolved: strip from the losers only, keep on the winner.
                    foreach (var loser in list)
                        if (loser != winners[0])
                            AddExclusion(toExclude, loser, pair.Key);
                }
                else
                {
                    // Genuine same-precedence collision: strip from every exposer.
                    foreach (var module in list)
                        AddExclusion(toExclude, module, pair.Key);
                    genuineCollisions.Add(pair.Key);
                }
            }

            foreach (var pair in toExclude)
                pair.Key.ExcludeFieldNames(pair.Value);

            // Recorded each pass so a resolved collision clears on the next Run(). Only genuine ties
            // count; a precedence-resolved supersede is expected, not an error.
            LastFieldNameCollisions = genuineCollisions.OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        private static void AddExclusion(Dictionary<TsModule, HashSet<string>> toExclude, TsModule module, string name)
        {
            if (!toExclude.TryGetValue(module, out var set))
                toExclude[module] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(name);
        }

        // Field names dropped by the most recent Run() because more than one module tried to
        // expose the same name, for example a Global and a Construct both deriving
        // "GameManager". Surfaced in TsWindow as a warning instead of only the console
        // Debug.LogError each affected module already logs in its ExcludeFieldNames() override.
        internal static IReadOnlyList<string> LastFieldNameCollisions { get; private set; } = Array.Empty<string>();

        // Names ApplyTreeShaking excluded across every module during the most recent Run() pass,
        // in module order. Empty whenever tree-shaking is off or nothing was excluded.
        internal static IReadOnlyList<string> LastTreeShakingExclusions { get; private set; } = Array.Empty<string>();

        // Names kept this pass only via the tree-shaking grace period: unreferenced right now,
        // but not yet excluded since this is the first pass they've been seen that way.
        internal static IReadOnlyList<string> LastTreeShakingGraceIncluded { get; private set; } = Array.Empty<string>();

        // Deletes any *.cs file directly under TsPaths.GeneratedFolder that carries tsvrc's own
        // auto-generated header but doesn't match any current module's FileName. Only files with
        // that exact header are touched, so a hand-added file sharing this folder is never at
        // risk. Runs on every pass regardless of compile state, since a stale duplicate-defining
        // file is often the actual cause of a compile break, not just something to tidy up once
        // healthy. Returns true if anything was deleted, so the caller knows a refresh is needed.
        private static bool DeleteOrphanedGeneratedFiles(List<TsModule> modules)
        {
            string folder = TsPaths.ToFullPath(TsPaths.GeneratedFolder);
            if (!Directory.Exists(folder)) return false;

            var expected = new HashSet<string>(
                modules.Where(m => m.FileName != null).Select(m => m.FileName),
                StringComparer.Ordinal);

            bool deletedAny = false;
            foreach (string fullPath in Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(fullPath);
                if (expected.Contains(fileName)) continue;

                string content;
                try { content = File.ReadAllText(fullPath, Encoding.UTF8); }
                catch { continue; }
                if (!content.StartsWith("// <auto-generated/>")) continue;

                try
                {
                    File.Delete(fullPath);
                    string metaPath = fullPath + ".meta";
                    if (File.Exists(metaPath)) File.Delete(metaPath);
                    deletedAny = true;
                    Debug.LogWarning($"[Tsvrc] Deleted orphaned generated file '{fileName}' - no module produces " +
                        "it anymore (it was likely renamed or removed in a tsvrc update). If this looks wrong, " +
                        "check for an unrelated hand-added file that happens to share this folder.");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Tsvrc] Failed to delete orphaned generated file '{fileName}': {e.Message}.");
                }
            }
            return deletedAny;
        }

        private static bool WriteModules(List<TsModule> modules)
        {
            bool written = false;
            foreach (var module in modules)
            {
                if (module.FileName == null) continue;
                // A locked file, a full disk, or a read-only checkout degrades to "this pass
                // didn't complete, try again next time" instead of an uncaught exception aborting
                // the loop mid-pass. Stops attempting further modules rather than compounding the
                // uncertainty; whichever modules already succeeded remain individually complete
                // and valid (see WriteIfChanged's atomic write below), and the normal
                // hierarchyChanged/domain-reload triggers retry a future pass on their own.
                try
                {
                    written |= WriteIfChanged($"{TsPaths.GeneratedFolder}/{module.FileName}", module.GenerateCode());
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Tsvrc] Failed to write '{module.FileName}': {e.Message}. Fix the underlying " +
                        "issue (e.g. a locked file, a full disk, a read-only checkout) - this will retry " +
                        "automatically on the next change, or via Tsvrc > Force Regenerate.");
                    return written;
                }
            }
            return written;
        }

        // Content comparison before writing avoids touching the file when output is identical,
        // which would otherwise trigger an AssetDatabase.Refresh() and a full reimport cycle
        // on every Run() even when nothing changed.
        private static bool WriteIfChanged(string assetPath, string content)
        {
            string fullPath = TsPaths.ToFullPath(assetPath);
            if (File.Exists(fullPath) && File.ReadAllText(fullPath, Encoding.UTF8) == content)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            // Written to a temp file first, then atomically swapped into place, so a crash or
            // power loss exactly mid-write can never leave a truncated file behind: fullPath is
            // always either the complete previous version or the complete new one. The .tmp file
            // itself isn't written atomically, but nothing ever reads it directly, so a crash in
            // the narrow window before the swap only leaves a harmless stray .tmp on disk.
            string tempPath = fullPath + ".tmp";
            File.WriteAllText(tempPath, content, Encoding.UTF8);
            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, null);
            else
                File.Move(tempPath, fullPath);
            return true;
        }
    }
}
#endif
