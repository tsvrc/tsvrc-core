#if UNITY_EDITOR
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace Tsvrc.Editor
{
    // Resolves this package's own root folder as a Unity project-relative path
    // ("Assets/Tsvrc" or "Packages/com.tsvrc.core", depending on how it was installed),
    // so the rest of the generator never has to hardcode "Assets/Tsvrc/...".
    //
    // [CallerFilePath] is filled in by the compiler with the absolute disk path of the
    // call site, not the method declaration - so this only works because ComputeRoot()
    // is called exactly once, from the line directly below, inside this same file. Any
    // other caller must go through Root, never call ComputeRoot() directly.
    internal static class PackagePaths
    {
        private static string _root;

        internal static string Root => _root ??= ComputeRoot();

        private static string ComputeRoot([CallerFilePath] string thisFilePath = "")
        {
            // thisFilePath: <PackageRoot>/Editor/CodeGen/PackagePaths.cs
            string codeGenDir = Path.GetDirectoryName(thisFilePath);
            string editorDir = Path.GetDirectoryName(codeGenDir);
            string packageRoot = Path.GetDirectoryName(editorDir);
            return FileUtil.GetProjectRelativePath(packageRoot.Replace('\\', '/') + "/").TrimEnd('/');
        }
    }
}
#endif
