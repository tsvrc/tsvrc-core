#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Config;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Abstract base for all generator passes. Each module is responsible for one slice
    // of the TsGenerated partial class: generating its code fragment, wiring scene
    // references into serialized fields, and watching the assets that affect its output.
    //
    // TsGenerator calls modules in a fixed sequence each run: LoadConfig, then optionally
    // ExposedFieldNames and ExcludeFieldNames, then GenerateCode, then AfterFilesStable,
    // then Wire. Modules must not depend on each other's in memory state. Only shared scene
    // state, read through FindRoot or FindObjectOfType, is safe to read across modules.
    internal abstract class TsModule
    {
        // internal, not protected: TsGenerator and TsWindow also need this (see
        // IsBuiltinConfigMissing below), not just the modules that read builtin entries from it.
        internal static string BuiltinConfigPath => $"{PackagePaths.Root}/Runtime/Config/TsBuiltinConfig.asset";

        // Singleton/Pool/Factory each treat a missing TsBuiltinConfig as "no builtins" with no
        // warning. Checking it once here lets RunCore log a single shared warning instead of
        // three identical ones, and lets TsWindow surface it as a persistent status.
        internal static bool IsBuiltinConfigMissing() =>
            AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>(BuiltinConfigPath) == null;

        // Null when a module contributes no generated file of its own, for example when it only
        // wires data into another module's class. WriteModules() skips writing in that case.
        internal virtual string FileName => null;
        internal virtual IEnumerable<string> WatchedAssets() => Enumerable.Empty<string>();
        // Short type names, such as "GameManager", whose serialized property modifications
        // should trigger a generator rerun. Populated by modules that wire references onto
        // user behaviours.
        internal virtual IEnumerable<string> WatchedComponentTypeNames() => Enumerable.Empty<string>();

        internal abstract void LoadConfig();
        internal virtual string GenerateCode() => null;
        internal virtual bool AfterFilesStable() => false;
        internal virtual void Wire() { }
        internal virtual bool OnSceneHierarchyChanged() => false;

        // Returns field names this module will declare on TsGenerated (via a partial).
        // Used by TsGenerator to detect cross-module naming conflicts after LoadConfig().
        internal virtual IEnumerable<string> ExposedFieldNames() => Enumerable.Empty<string>();

        // Called with the set of conflicting names so the module can remove them and log errors.
        internal virtual void ExcludeFieldNames(IEnumerable<string> names) { }

        // Non-null shows this module as a tab in TsWindow, labeled TabLabel, described by
        // TabDescription, drawn by DrawTab.
        internal virtual string TabLabel => null;
        internal virtual string TabDescription => null;
        internal virtual void DrawTab(SerializedObject so) { }

        // Names ApplyTreeShaking excluded this pass, empty when tree-shaking is off or nothing was
        // excluded. TsGenerator aggregates every module's list for TsWindow to display.
        internal virtual IEnumerable<string> LastTreeShakingExclusions => Enumerable.Empty<string>();

        // Names kept this pass only via ConsumeGracePeriod below: genuinely unreferenced, but not
        // yet excluded since this is the first pass they've been seen that way. Kept separate from
        // LastTreeShakingExclusions so TsWindow can show "newly unused, kept for now" apart from
        // "actually removed."
        internal virtual IEnumerable<string> LastTreeShakingGraceIncluded => Enumerable.Empty<string>();

        // Returns null if the compiled type does not yet exist or has no instance in the scene.
        protected static Component FindRoot()
        {
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType == null) return null;
            return TsLinkedScene.FindType(compiledType);
        }

        // Tries reflection first: it is fast, exact, and correct even if two differently based
        // types happen to share a simple name. If the type is loaded, this walks its real
        // BaseType chain, comparing by simple name only rather than full type identity, because
        // Unity's AppDomain can carry stale duplicate copies of the same assembly across
        // successive recompiles, which would otherwise make a real match fail an exact type
        // identity check.
        //
        // Falls back to the source text based ScriptIndex only when the type isn't loaded in
        // any assembly at all. That typically means Assembly-CSharp currently has a compile
        // error elsewhere, exactly the scenario a not yet generated TsGenerated member causes,
        // and without this fallback the lookup would silently resolve to "not a
        // TsvrcBehaviour", dropping a real TsConstruct() call from the generated output.
        protected static bool IsTsvrcBehaviourType(string shortName, string ns)
        {
            string fullName = string.IsNullOrEmpty(ns) ? shortName : $"{ns}.{shortName}";
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type == null) continue;
                for (var t = type.BaseType; t != null; t = t.BaseType)
                    if (t.Name == "TsvrcBehaviour") return true;
                return false;
            }
            return ScriptIndex.DerivesFrom(shortName, "TsvrcBehaviour", ns);
        }

        // Resolves the type name + namespace backing a live scene Object reference, preferring
        // fast live reflection but falling back to ScriptIndex - source text scanning,
        // independent of compile state - when reflection can't produce a real answer. A
        // "Missing (Mono Script)" component, typically a world script that has never yet been
        // part of a successfully compiled assembly because it needs a field this very generator
        // pass is responsible for producing, always reports GetType() as exactly
        // typeof(MonoBehaviour), never a concrete subclass, so that exact check is what signals
        // "hand off to the fallback" rather than a null check. A GameObject or any other Object
        // type always reflects normally and never needs the fallback. Mirrors
        // IsTsvrcBehaviourType's identical two-tier shape, applied to resolving a type's
        // identity instead of testing its base-class membership.
        // internal, not protected: ObjectListGUI (not a TsModule subclass) also calls this
        // directly, to show a user which type tsvrc will actually resolve a reference to even
        // while the project doesn't currently compile - see its own DrawResolvedTypeHint.
        internal static bool TryResolveObjectType(UnityEngine.Object obj, out string typeName, out string ns)
        {
            typeName = null;
            ns = null;
            if (obj == null) return false;

            var type = obj.GetType();
            if (!(obj is Component) || type != typeof(MonoBehaviour))
            {
                typeName = type.Name;
                ns = type.Namespace ?? string.Empty;
                return true;
            }

            return TryResolveViaScript((Component)obj, out typeName, out ns);
        }

        // Split out from TryResolveObjectType so tests can exercise the ScriptIndex-backed
        // fallback directly against a real component's real MonoScript, without needing to
        // construct an actual "Missing (Mono Script)" component - Unity provides no supported
        // way to do that from editor script (AddComponent<MonoBehaviour> is rejected as
        // abstract-for-attachment).
        protected static bool TryResolveViaScript(Component component, out string typeName, out string ns)
        {
            typeName = null;
            ns = null;
            if (component == null) return false;

            var so = new SerializedObject(component);
            var scriptProp = so.FindProperty("m_Script");
            var script = scriptProp?.objectReferenceValue as MonoScript;
            return ScriptIndex.TryResolveDeclaredType(script, out typeName, out ns);
        }

        // If the current pass resolved fewer live entries than the last known good snapshot
        // while the project's compile is currently broken, the live result is untrustworthy: a
        // broken Assembly-CSharp nulls out every scene reference to a component declared in it.
        // In that case this falls back to the snapshot instead of silently shrinking
        // GenerateCode()'s output. Otherwise, on a clean compile or when the live count is
        // already at least as large as the snapshot's, it trusts the live result and refreshes
        // the snapshot from it.
        //
        // Callers need to know one thing about the contract here: a restored entry's
        // fromSnapshot delegate has no way to recover an actual UnityEngine.Object scene or
        // prefab reference from a name alone, so callers correctly construct it with that
        // reference left null (see SingletonModule, FactoryModule, and ConstructModule's
        // fromSnapshot lambdas). This protects GenerateCode()'s output, meaning field
        // declarations and TsConstruct() calls, across a transient broken compile, but Wire()
        // cannot re-wire the scene field for a restored entry. It harmlessly writes null into
        // it until the next clean compile refreshes the snapshot with real objects.
        // treeShakingExclusions: how many entries ApplyTreeShaking already removed from resolved
        // this same pass, as a deliberate exclusion rather than a compile break or accidental
        // deletion. Defaults to 0 for modules that don't tree-shake. See WarnIfBelowLastKnownGood.
        protected static List<TEntry> ApplySnapshotFallback<TEntry>(
            string moduleKey,
            List<TEntry> resolved,
            Func<TEntry, ModuleEntrySnapshot.Entry> toSnapshot,
            Func<ModuleEntrySnapshot.Entry, TEntry> fromSnapshot,
            int treeShakingExclusions = 0)
        {
            if (TsPaths.ScriptCompilationFailed)
            {
                var cached = ModuleEntrySnapshot.Load(moduleKey);
                if (cached != null && cached.Count > resolved.Count)
                {
                    Debug.LogWarning($"[{moduleKey}] Compile errors are present and live resolution found fewer entries " +
                        $"({resolved.Count}) than the last known-good snapshot ({cached.Count}) - using the snapshot instead " +
                        "of overwriting real generated content with a reduced/empty result. Fix the compile errors and " +
                        "regenerate once clean to update this snapshot.");
                    return cached.Select(fromSnapshot).ToList();
                }
                return resolved;
            }

            WarnIfBelowLastKnownGood(moduleKey, resolved.Count, treeShakingExclusions);
            ModuleEntrySnapshot.Save(moduleKey, resolved.Select(toSnapshot).ToList());
            return resolved;
        }

        // A clean compile with fewer entries than last time is treated as the user genuinely
        // wanting them gone (see the compile-broken branch above, which this doesn't touch), but
        // that also makes an accidental deletion (e.g. the TsConfig GameObject removed from the
        // Hierarchy) invisible, since the pass that loses the data looks like an ordinary
        // regenerate. This can't block the pass outright, since a deliberate deletion via
        // Configure's own delete button must stay fast, so it warns instead: a rolling "last
        // known good" count, tracked separately from the compile-broken snapshot above, that only
        // ever rises. A drop below it is always a real regression relative to the true high-water
        // mark, never a target that quietly moves down to match whatever just happened.
        // A drop fully explained by this pass's own tree-shaking exclusions is expected and must
        // not trigger the warning below (it would otherwise be indistinguishable from an
        // accidental deletion). Only the unattributed remainder of a drop still warns.
        private static void WarnIfBelowLastKnownGood(string moduleKey, int resolvedCount, int treeShakingExclusions)
        {
            string lastKnownGoodKey = LastKnownGoodSnapshotKey(moduleKey);
            int? lastKnownGood = ModuleEntrySnapshot.LoadCount(lastKnownGoodKey);

            if (lastKnownGood.HasValue && lastKnownGood.Value > resolvedCount)
            {
                int drop = lastKnownGood.Value - resolvedCount;
                if (drop > treeShakingExclusions)
                    Debug.LogWarning($"[{moduleKey}] This regenerate resolved fewer entries ({resolvedCount}) than the " +
                        $"last known-good count ({lastKnownGood.Value}), on an otherwise clean compile. If this wasn't " +
                        "intentional (for example the TsConfig object was accidentally deleted from the Hierarchy), " +
                        "check Tsvrc > Configure and Undo before this state is overwritten again.");
            }

            if (!lastKnownGood.HasValue || resolvedCount >= lastKnownGood.Value)
                ModuleEntrySnapshot.SaveCount(lastKnownGoodKey, resolvedCount);
        }

        // Filters resolved down to entries referenced by project source (via isReferenced) or
        // explicitly pinned via config.ForceIncludeNames, when config.TreeShakeUnused is on. A
        // no-op when config is null or TreeShakeUnused is off. Shared by every tree-shaking module
        // (Singleton, Factory, and Log/Memory via a single-element list) instead of each
        // hand-rolling the same filter.
        //
        // nameOf/isReferenced take the entry's already-resolved generated name rather than its
        // source object, since usage is a fact about the chosen identifier, independent of where
        // the entry came from.
        //
        // Every unreferenced-and-not-force-included entry goes through ConsumeGracePeriod before
        // being excluded for real, so an entry is only removed once it's been observed unreferenced
        // on two separate, counting passes, never the first time (see ConsumeGracePeriod).
        //
        // countsForGracePeriod is false for a pass caused only by a reactive trigger unrelated to
        // the entry being evaluated: the result is still shown, but must not consume or reset any
        // grace progress, since the developer had no real opportunity to reference it this pass.
        // Defaults to true so a direct call (every module's own LoadConfig(), or a test calling
        // this without going through TsGenerator.Run()) is its own real, counting pass.
        protected static List<TEntry> ApplyTreeShaking<TEntry>(
            TsConfig config,
            string moduleTag,
            List<TEntry> resolved,
            Func<TEntry, string> nameOf,
            Func<string, bool> isReferenced,
            out int excludedCount,
            out List<string> excludedNames,
            out List<string> graceIncludedNames,
            bool countsForGracePeriod = true)
        {
            excludedCount = 0;
            excludedNames = new List<string>();
            graceIncludedNames = new List<string>();
            if (config == null || !config.TreeShakeUnused)
            {
                // Cleared, not left stale: re-enabling tree-shaking later should give every entry
                // a fresh grace period, not silently resume a miss-streak that started months ago
                // while the feature was off.
                SaveGraceState(moduleTag, Enumerable.Empty<string>());
                return resolved;
            }

            var forceIncludeNames = new HashSet<string>(config.ForceIncludeNames ?? Array.Empty<string>(), StringComparer.Ordinal);

            // A live result decides anything (advances a miss-streak, resets one, or excludes an
            // entry) only when the pass counts and the project compiles cleanly, mirroring
            // ApplySnapshotFallback's own compile-health gate. Otherwise unreferenced entries are
            // still shown as grace-included, but persisted grace state is left untouched.
            bool evaluateForReal = countsForGracePeriod && !TsPaths.ScriptCompilationFailed;

            var previouslyUnreferenced = evaluateForReal ? LoadGraceState(moduleTag) : null;
            var stillUnreferenced = evaluateForReal ? new HashSet<string>(StringComparer.Ordinal) : null;
            var kept = new List<TEntry>();

            foreach (var entry in resolved)
            {
                string name = nameOf(entry);
                if (forceIncludeNames.Contains(name) || isReferenced(name))
                {
                    kept.Add(entry);
                    continue;
                }

                if (!evaluateForReal || !ConsumeGracePeriod(name, previouslyUnreferenced, stillUnreferenced))
                {
                    kept.Add(entry);
                    graceIncludedNames.Add(name);
                    continue;
                }

                excludedCount++;
                excludedNames.Add(name);
                Debug.Log($"[{moduleTag}] Excluded '{name}' - not referenced anywhere in the project (checked across " +
                    "two regenerates) and not force-included. Reference it from a TsvrcBehaviour, or add it to Force " +
                    "Include Names in Configure, to keep generating it.");
            }

            if (evaluateForReal)
                SaveGraceState(moduleTag, stillUnreferenced);
            if (graceIncludedNames.Count > 0)
            {
                string plural = graceIncludedNames.Count == 1 ? "entry isn't" : "entries aren't";
                string pronoun = graceIncludedNames.Count == 1 ? "it" : "them";
                Debug.Log($"[{moduleTag}] {graceIncludedNames.Count} {plural} referenced anywhere in the project yet, " +
                    $"kept for now, pending one more regenerate: {string.Join(", ", graceIncludedNames)}. Reference " +
                    $"{pronoun} soon, or {pronoun} may be excluded next time.");
            }
            return kept;
        }

        // A name is excluded only once it's been observed unreferenced on two separate,
        // consecutive *counting* passes, never the first time. A newly-registered entry (or one
        // whose last reference just disappeared) is otherwise indistinguishable from a bug:
        // nothing can reference "_ts.Foo" in code before Foo has been generated at least once.
        // Keeping it for one extra counting pass gives a developer the normal edit-save-recompile
        // cycle to write (or restore) the reference before it's actually removed.
        private static bool ConsumeGracePeriod(string name, HashSet<string> previouslyUnreferenced, HashSet<string> stillUnreferencedThisPass)
        {
            stillUnreferencedThisPass.Add(name);
            return previouslyUnreferenced.Contains(name);
        }

        private static HashSet<string> LoadGraceState(string moduleKey) => ModuleEntrySnapshot.LoadNames(TreeShakeGraceKey(moduleKey));

        private static void SaveGraceState(string moduleKey, IEnumerable<string> names) => ModuleEntrySnapshot.SaveNames(TreeShakeGraceKey(moduleKey), names);

        private static string TreeShakeGraceKey(string moduleKey) => $"{moduleKey}.TreeShakeGrace";

        private static string LastKnownGoodSnapshotKey(string moduleKey) => $"{moduleKey}.LastKnownGood";

        // Extracts the alias text from a __Alias__ GameObject name convention.
        // Returns null if the name does not follow the convention.
        protected static string AliasName(string goName)
        {
            if (goName != null && goName.StartsWith("__") && goName.EndsWith("__") && goName.Length > 4)
                return goName.Substring(2, goName.Length - 4);
            return null;
        }

        // Appends a numeric suffix (2, 3, ...) until the name is not in usedNames.
        // Suffix starts at 2 so the first collision reads "Foo 2" rather than "Foo 1",
        // matching the convention used by OS file copy dialogs and Unity's own asset
        // duplication behaviour.
        protected static string Deduplicate(string baseName, HashSet<string> usedNames)
        {
            string name = baseName;
            int suffix = 2;
            while (usedNames.Contains(name))
                name = $"{baseName}{suffix++}";
            return name;
        }

        // Unity does not auto dirty the scene when SerializedObject properties are changed
        // in editor code. Both calls are required for the change to survive a save.
        protected static void ApplyAndMarkDirty(SerializedObject so, Component root)
        {
            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }

        // The "no entries" fallback every module with a FileName falls back to: header, optional
        // usings, the namespace/class wrapper, and zero or more empty methods inside it. usings
        // may be null to omit the using block entirely (matching a module with no dependencies).
        protected static string BuildStub(IEnumerable<string> usings, params string[] emptyMethodSignatures)
        {
            var w = new UdonWriter();
            w.AutoGenHeader();
            w.BlankLine();
            if (usings != null) w.Usings(usings);
            using (w.Namespace(ScaffoldModule.CompiledNamespace))
            using (w.Block($"public partial class {ScaffoldModule.CompiledClassName}"))
                foreach (var signature in emptyMethodSignatures)
                    using (w.Method(signature)) { }
            return w.ToString();
        }

        // Resolves a serialized field by name during Wire(), logging the module's standard
        // "force compile to regenerate" warning when it's missing - for example because the
        // field was only just added to GenerateCode()'s output and the project hasn't recompiled
        // yet. Callers should skip that entry rather than throw.
        protected static bool TryFindField(SerializedObject so, string fieldName, string moduleTag, out SerializedProperty prop)
        {
            prop = so.FindProperty(fieldName);
            if (prop != null) return true;
            Debug.LogWarning($"[{moduleTag}] Field '{fieldName}' not found on {ScaffoldModule.CompiledClassName}. Force compile to regenerate.");
            return false;
        }

        // Null and duplicate config entries share the same skip-and-warn shape across modules
        // that resolve a scene/asset object list (Singletons, Constructs): a null slot means a
        // referenced object was deleted, a repeat means the same object was dragged in twice.
        // Both are user config mistakes to fix, not generator bugs, so they're logged and
        // skipped rather than thrown. configLabel/entryNoun preserve each module's own existing
        // wording ("config" vs "Constructs config", "entry" vs "construct") exactly.
        protected static bool TryAcceptEntry(UnityEngine.Object obj, string moduleTag, string configLabel, string entryNoun, HashSet<UnityEngine.Object> seen)
        {
            if (obj == null)
            {
                Debug.LogWarning($"[{moduleTag}] Null entry in {configLabel}, remove the missing-script slot.");
                return false;
            }
            if (!seen.Add(obj))
            {
                Debug.LogWarning($"[{moduleTag}] Duplicate {entryNoun} '{obj.name}' in config, remove the duplicate.");
                return false;
            }
            return true;
        }
    }
}
#endif
