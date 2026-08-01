using System.IO;
using UnityEditor;

namespace Tsvrc.Tests.EditMode
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
            {
                AssetDatabase.DeleteAsset(Folder);
                return;
            }
            // AssetDatabase may not know this folder exists at all if something wrote into it via
            // raw File/Directory I/O without ever refreshing first (ModuleEntrySnapshot's own
            // cache writes do exactly this), or a test redirected TsPaths.GeneratedFolder here
            // without calling EnsureFolder() first. Delete straight from disk too, including a
            // stray .meta, so a leak like that can never survive past this test's own TearDown.
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, recursive: true);
            if (File.Exists(Folder + ".meta"))
                File.Delete(Folder + ".meta");
        }
    }
}
