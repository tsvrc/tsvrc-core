#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Core;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    internal class ScaffoldModule : TsvrcModule
    {
        // Shared across the assembly — other modules and the generator reference these.
        internal const string ConfigPath = "Assets/TsvrcGenerated/CoreConfig.asset";
        internal const string CompiledNamespace = "Tsvrc.Core.Generated";
        internal const string CompiledClassName = "TsvrcGenerated";
        internal const string CompiledTypeFullName = CompiledNamespace + "." + CompiledClassName + ", Assembly-CSharp";

        private const string ScaffoldFilePath = "Assets/TsvrcGenerated/TsvrcGenerated.cs";
        private const string GeneratedAssetPath = "Assets/TsvrcGenerated/TsvrcGenerated.asset";

        private readonly IReadOnlyList<TsvrcModule> _contentModules;

        internal ScaffoldModule(IReadOnlyList<TsvrcModule> contentModules)
        {
            _contentModules = contentModules;
        }

        internal override string FileName => "TsvrcGenerated.cs";

        internal override string GenerateCode() => BuildScaffold(_contentModules);

        internal override void AfterFilesStable()
        {
            LoadOrCreateConfig();
            EnsureUdonSharpProgramAsset();
            EnsureSceneObject();
        }

        internal static TsvrcConfig2 LoadOrCreateConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TsvrcConfig2>(ConfigPath);
            if (existing != null) return existing;

            var config = ScriptableObject.CreateInstance<TsvrcConfig2>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TsvrcGenerator] Created config at {ConfigPath}");
            return config;
        }

        private static void EnsureUdonSharpProgramAsset()
        {
            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(ScaffoldFilePath);
            if (monoScript == null) return;

            var programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(GeneratedAssetPath);
            if (programAsset == null)
            {
                programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.sourceCsScript = monoScript;
                AssetDatabase.CreateAsset(programAsset, GeneratedAssetPath);
                AssetDatabase.SaveAssetIfDirty(programAsset);
                Debug.Log($"[TsvrcGenerator] Created program asset at {GeneratedAssetPath}");
                return;
            }

            if (programAsset.sourceCsScript != monoScript)
            {
                programAsset.sourceCsScript = monoScript;
                EditorUtility.SetDirty(programAsset);
                AssetDatabase.SaveAssetIfDirty(programAsset);
            }
        }

        private static void EnsureSceneObject()
        {
            var compiledType = Type.GetType(CompiledTypeFullName);
            if (compiledType == null) return;

            if (UnityEngine.Object.FindObjectOfType(compiledType, true) != null) return;

            var go = new GameObject(CompiledClassName);
            go.transform.SetSiblingIndex(0);
            Undo.RegisterCreatedObjectUndo(go, $"Create {CompiledClassName}");
            UdonSharpUndo.AddComponent(go, compiledType);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[TsvrcGenerator] Created {CompiledClassName} in scene.");
        }

        private static string BuildScaffold(IReadOnlyList<TsvrcModule> contentModules)
        {
            var w = new CsWriter();
            w.AutoGenHeader();
            w.BlankLine();
            w.Usings(new[] { "UdonSharp", "UnityEngine" });

            using (w.Namespace(CompiledNamespace))
            {
                // [AddComponentMenu("")] hides this generated type from Unity's Add Component menu.
                w.Line("[AddComponentMenu(\"\")]");
                using (w.Block($"public partial class {CompiledClassName} : UdonSharpBehaviour"))
                {
                    var startCalls = contentModules
                        .Select(m => m.StartMethodCall)
                        .Where(c => c != null)
                        .ToList();

                    if (startCalls.Count > 0)
                    {
                        using (w.Method("protected void Start()"))
                            foreach (var call in startCalls)
                                w.Line($"{call};");
                    }
                }
            }

            return w.ToString();
        }
    }
}
#endif
