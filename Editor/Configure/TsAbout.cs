#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Tsvrc > Documentation opens the website; Tsvrc > About shows name/version/description from
    // package.json, so a user reporting a bug doesn't have to dig through Package Manager or the raw file.
    internal static class TsAbout
    {
        private const string DocumentationUrl = "https://tsvrc.com";

        [System.Serializable]
        private class PackageInfo
        {
            public string displayName;
            public string version;
            public string description;
        }

        [MenuItem("Tsvrc/Documentation", priority = 61)]
        private static void OpenDocumentation() => Application.OpenURL(DocumentationUrl);

        [MenuItem("Tsvrc/About", priority = 62)]
        private static void ShowAbout()
        {
            string fullPath = ToFullPath($"{PackagePaths.Root}/package.json");
            string json = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
            EditorUtility.DisplayDialog("About Tsvrc", DetermineAboutMessage(json), "OK");
        }

        // Pure so it's directly unit-testable without touching disk or EditorUtility.
        internal static string DetermineAboutMessage(string packageJson)
        {
            if (string.IsNullOrEmpty(packageJson))
                return "Tsvrc\n\n(package.json not found - version information unavailable.)";

            PackageInfo info;
            try { info = JsonUtility.FromJson<PackageInfo>(packageJson); }
            catch { info = null; }

            if (info == null || string.IsNullOrEmpty(info.version))
                return "Tsvrc\n\n(package.json could not be parsed - version information unavailable.)";

            string name = string.IsNullOrEmpty(info.displayName) ? "Tsvrc" : info.displayName;
            string message = $"{name}\nVersion {info.version}";
            if (!string.IsNullOrEmpty(info.description))
                message += $"\n\n{info.description}";
            return message;
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
#endif
