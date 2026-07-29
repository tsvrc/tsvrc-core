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
    // TsGenerator calls modules in a fixed sequence each run:
    //   LoadConfig -> (optional) ExposedFieldNames/ExcludeFieldNames → GenerateCode →
    //   AfterFilesStable → Wire
    // Modules must not depend on each other's in-memory state; only shared scene state
    // (FindRoot, FindObjectOfType) is safe to read across modules.
    internal abstract class TsModule
    {
        protected static string BuiltinConfigPath => $"{PackagePaths.Root}/Runtime/Config/TsBuiltinConfig.asset";

        // Null when a module contributes no generated file of its own (e.g. it only wires data
        // into another module's class). WriteModules() skips writing in that case.
        internal virtual string FileName => null;
        internal virtual IEnumerable<string> WatchedAssets() => Enumerable.Empty<string>();
        // Short type names (e.g. "GameManager") whose serialized-property modifications should
        // trigger a generator rerun. Populated by modules that wire references onto user behaviours.
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

        protected static bool IsTsBehaviourType(string shortName, string ns)
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
            return false;
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

        // Unity does not auto-dirty the scene when SerializedObject properties are changed
        // in editor code — both calls are required for the change to survive a save.
        protected static void ApplyAndMarkDirty(SerializedObject so, Component root)
        {
            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }
    }
}
#endif
