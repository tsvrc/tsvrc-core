using System.IO;
using UnityEditor;

namespace Tsvrc.Tests.Editor
{
    // Scratch-asset convention: any test that must create real .asset/prefab files
    // (program assets, factory/pool prefabs) writes them under this folder and deletes
    // the whole thing afterward, so no leftover asset ever pollutes the actual project.
    internal static class ScratchAssets
    {
        internal const string Folder = "Assets/Tsvrc/Tests/Editor/CodeGen/__Scratch__";

        internal static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder)) return;
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
        }

        internal static void DeleteAll()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
        }
    }
}
