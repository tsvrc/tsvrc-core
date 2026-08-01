#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
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
        protected static string BuiltinConfigPath => $"{PackagePaths.Root}/Runtime/Config/TsBuiltinConfig.asset";

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

        // Returns null if the compiled type does not yet exist or has no instance in the scene.
        protected static Component FindRoot()
        {
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType == null) return null;
            return (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
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
        protected static bool TryResolveObjectType(UnityEngine.Object obj, out string typeName, out string ns)
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
        protected static List<TEntry> ApplySnapshotFallback<TEntry>(
            string moduleKey,
            List<TEntry> resolved,
            Func<TEntry, ModuleEntrySnapshot.Entry> toSnapshot,
            Func<ModuleEntrySnapshot.Entry, TEntry> fromSnapshot)
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

            ModuleEntrySnapshot.Save(moduleKey, resolved.Select(toSnapshot).ToList());
            return resolved;
        }

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
    }
}
#endif
