#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    internal static class TsvrcGenerator
    {
        private const string GeneratedFolder = "Assets/TsvrcGenerated";
        private const string CacheFolder = "Assets/.tsvrc";

        [MenuItem("Tsvrc/V2/Manual Compile")]
        public static void Compile() => AfterDomainReload();

        internal static void AfterDomainReload()
        {
            var modules = CreateModules();

            foreach (var module in modules)
                module.OnDomainReloaded();

            bool anyWritten = false;
            foreach (var module in modules)
            {
                if (module.FileName == null) continue;
                var path = $"{GeneratedFolder}/{module.FileName}";
                var code = module.GenerateCode();
                anyWritten |= string.IsNullOrEmpty(code) ? DeleteIfExists(path) : WriteIfChanged(path, code);
            }

            if (anyWritten)
            {
                // AssetDatabase.Refresh() triggers a domain reload; AfterFilesStable runs on the next stable pass.
                AssetDatabase.Refresh();
                return;
            }

            foreach (var module in modules)
                module.AfterFilesStable();

            RunWire(modules);
        }

        internal static List<TsvrcModule> CreateModules()
        {
            var contentModules = new List<TsvrcModule> { new PoolModule(), new TranslationModule() };
            var all = new List<TsvrcModule>(contentModules.Count + 1) { new ScaffoldModule(contentModules) };
            all.AddRange(contentModules);
            return all;
        }

        internal static string ReadCacheFile(string fileName)
        {
            string fullPath = ToFullPath($"{CacheFolder}/{fileName}");
            return File.Exists(fullPath) ? File.ReadAllText(fullPath, Encoding.UTF8) : null;
        }

        internal static void WriteCacheFile(string fileName, string content)
        {
            string fullPath = ToFullPath($"{CacheFolder}/{fileName}");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content, Encoding.UTF8);
        }

        private static void RunWire(List<TsvrcModule> modules)
        {
            var compiledType = Type.GetType(ScaffoldModule.CompiledTypeFullName);
            if (compiledType == null) return;

            var component = (Component)UnityEngine.Object.FindObjectOfType(compiledType, true);
            if (component == null) return;

            var so = new SerializedObject(component);
            foreach (var module in modules)
                module.Wire(so);
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        private static bool WriteIfChanged(string assetPath, string content)
        {
            string fullPath = ToFullPath(assetPath);
            if (File.Exists(fullPath) && File.ReadAllText(fullPath, Encoding.UTF8) == content)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, content, Encoding.UTF8);
            return true;
        }

        private static bool DeleteIfExists(string assetPath)
        {
            if (!File.Exists(ToFullPath(assetPath))) return false;
            AssetDatabase.DeleteAsset(assetPath);
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
