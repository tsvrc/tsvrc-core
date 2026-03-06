#if UNITY_EDITOR
using System;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Creates/validates UdonSharp program assets for generated and auto-detected scripts.
    internal static class TsvrcProgramAssets
    {
        internal const string AccessorCsPath = "Assets/TsvrcGenerated/CompiledTsvrc.cs";
        internal const string AccessorAssetPath = "Assets/TsvrcGenerated/CompiledTsvrc.asset";
        internal const string InstanceCsPath = "Assets/TsvrcGenerated/CompiledTsvrcConfig.cs";
        internal const string InstanceAssetPath = "Assets/TsvrcGenerated/CompiledTsvrcConfig.asset";

        internal static void EnsureAsset(string csPath, string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                return; // already exists

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(csPath);
            if (monoScript == null)
            {
                Debug.LogError($"[TsvrcSceneWirer] MonoScript not found at '{csPath}'.");
                return;
            }

            var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = monoScript;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            Debug.Log($"[TsvrcSceneWirer] Created program asset '{assetPath}'.");
        }

        internal static void EnsureBothAssets()
        {
            EnsureAsset(AccessorCsPath, AccessorAssetPath);
            EnsureAsset(InstanceCsPath, InstanceAssetPath);
        }

        // Ensures a program asset exists for type before AddComponent is called.
        internal static void EnsureAssetForType(Type type)
        {
            MonoScript monoScript = null;
            foreach (var guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && ms.GetClass() == type)
                {
                    monoScript = ms;
                    break;
                }
            }

            if (monoScript == null)
            {
                Debug.LogError($"[TsvrcSceneWirer] Could not find MonoScript for type '{type.Name}'.");
                return;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                if (asset != null && asset.sourceCsScript == monoScript)
                    return;
            }

            var assetPath = $"Assets/TsvrcGenerated/{type.Name}.asset";
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath) != null)
                return;

            var programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            programAsset.sourceCsScript = monoScript;
            AssetDatabase.CreateAsset(programAsset, assetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TsvrcSceneWirer] Created program asset for '{type.Name}' at '{assetPath}'.");
        }
    }
}
#endif
