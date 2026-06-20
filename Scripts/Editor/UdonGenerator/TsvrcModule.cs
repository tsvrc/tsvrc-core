#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    internal abstract class TsvrcModule
    {
        protected const string BuiltinConfigPath = "Assets/Tsvrc/TsvrcBuiltinConfig.asset";

        // Null when a module contributes no generated file of its own (e.g. it only wires data
        // into another module's class). WriteModules() skips writing in that case.
        internal virtual string FileName => null;
        internal virtual IEnumerable<string> WatchedAssets() => Enumerable.Empty<string>();

        internal abstract void LoadConfig();
        internal virtual string GenerateCode() => null;
        internal virtual bool AfterFilesStable() => false;
        internal virtual void Wire() { }
        internal virtual bool OnSceneHierarchyChanged() => false;

        // Returns field names this module will declare on TsvrcGenerated (via a partial).
        // Used by TsvrcGenerator to detect cross-module naming conflicts after LoadConfig().
        internal virtual IEnumerable<string> ExposedFieldNames() => Enumerable.Empty<string>();

        // Called with the set of conflicting names so the module can remove them and log errors.
        internal virtual void ExcludeFieldNames(IEnumerable<string> names) { }

        // Finds the single TsvrcGenerated root component in the scene.
        // Returns null if the compiled type does not yet exist or has no instance in the scene.
        protected static Component FindRoot()
        {
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType == null) return null;
            return (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
        }

        // Checks whether a type identified by short name + namespace inherits from TsvrcBehaviour.
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

        // Appends a numeric suffix (2, 3, …) to baseName until it is not present in usedNames.
        protected static string Deduplicate(string baseName, HashSet<string> usedNames)
        {
            string name = baseName;
            int suffix = 2;
            while (usedNames.Contains(name))
                name = $"{baseName}{suffix++}";
            return name;
        }

        // Applies serialized property changes and marks the scene dirty when anything was modified.
        protected static void ApplyAndMarkDirty(SerializedObject so, Component root)
        {
            if (so.ApplyModifiedProperties())
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        }
    }
}
#endif
