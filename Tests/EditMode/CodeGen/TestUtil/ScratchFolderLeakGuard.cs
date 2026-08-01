using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Tsvrc.Tests.EditMode
{
    // Suite-wide safety net that fails loudly if any test leaves ScratchAssets.Folder, or a
    // leaked TsPaths redirect, behind instead of silently letting debris accumulate across
    // runs. Every individual test is responsible for its own cleanup, through
    // ScratchAssets.DeleteAll(), TsGeneratorTestHarness.Dispose(), or TempSceneScope.Dispose().
    // This only catches the case where one of them didn't.
    [SetUpFixture]
    public class ScratchFolderLeakGuard
    {
        [OneTimeTearDown]
        public void AssertNoScratchDebrisLeft()
        {
            // Checks the real filesystem too, not just AssetDatabase.IsValidFolder: raw file I/O
            // under this folder (ModuleEntrySnapshot's cache writes, or a test that redirected
            // TsPaths.GeneratedFolder here without calling EnsureFolder() first) can leave real
            // debris on disk that AssetDatabase never learned about, which would otherwise let
            // this check pass while a __Scratch__ folder still sits in the working tree.
            Assert.IsFalse(AssetDatabase.IsValidFolder(ScratchAssets.Folder) || Directory.Exists(ScratchAssets.Folder),
                $"'{ScratchAssets.Folder}' still exists after the full EditMode run - some test " +
                "created scratch assets and did not clean them up (ScratchAssets.DeleteAll() / " +
                "TsGeneratorTestHarness.Dispose() was skipped or threw before reaching it).");

            Assert.AreEqual(Tsvrc.Editor.TsPaths.DefaultGeneratedFolder, Tsvrc.Editor.TsPaths.GeneratedFolder,
                "TsPaths.GeneratedFolder was left redirected after the full EditMode run - some " +
                "test changed it without going through TempSceneScope/TsGeneratorTestHarness " +
                "disposal, which is what resets it.");
            Assert.AreEqual(Tsvrc.Editor.TsPaths.DefaultCompiledClassName, Tsvrc.Editor.TsPaths.CompiledClassName,
                "TsPaths.CompiledClassName was left redirected after the full EditMode run.");
        }
    }
}
