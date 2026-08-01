using System.IO;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEditor.SceneManagement;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.RunCore's TsLinkedScene.IsConfiguredButNotLoaded guard: once a scene is
    // linked, any pass - reactive or explicit, including a direct Run() call like tsvrc's own
    // CodeGen tests make - must be a hard no-op unless that exact scene is currently loaded.
    // TsLinkedSceneTests covers the property itself; this covers TsGenerator actually respecting it.
    public class TsGeneratorLinkedSceneGuardTests
    {
        private TsGeneratorTestHarness _harness;

        [SetUp]
        public void SetUp() => _harness = new TsGeneratorTestHarness();

        [TearDown]
        public void TearDown() => _harness.Dispose();

        [Test]
        public void Run_LinkedSceneConfiguredButNotLoaded_WritesNothingAndDoesNotThrow()
        {
            // TempSceneScope's own override (see its constructor) would otherwise make
            // TsLinkedScene.IsConfigured false regardless of what's set below - cleared here
            // since this test is specifically about the real, non-override ScenePath path.
            TsLinkedScene.ClearOverride();
            // Setting ScenePath itself creates GeneratedFolder, to hold TsLinkedSceneConfig.asset
            // - that's not what's under test here, so the assertion below checks for generated
            // .cs files specifically, not the folder's mere existence.
            TsLinkedScene.ScenePath = "Assets/DoesNotExist/Nowhere.unity";
            Assert.IsTrue(TsLinkedScene.IsConfiguredButNotLoaded, "Precondition for this test.");

            // allowBootstrap: true, matching how every other CodeGen test drives Run() directly,
            // so this test's own assertion isn't at the mercy of whether HasBootstrapSignal()
            // happens to be true independent of the guard under test here.
            Assert.DoesNotThrow(() => TsGenerator.Run(skipRefresh: true, allowBootstrap: true));

            CollectionAssert.IsEmpty(GeneratedCsFiles(),
                "RunCore must not write any generated .cs file when the linked scene isn't loaded.");
        }

        [Test]
        public void Run_LinkedSceneConfiguredAndLoaded_ProceedsNormallyAndWrites()
        {
            // An unsaved scene's path is always "", which would make IsConfigured false rather
            // than "configured and loaded" - saved to a real scratch path first so ScenePath can
            // point at it for real.
            string scenePath = ScratchAssets.Folder + "/LinkedSceneGuardTest.unity";
            EditorSceneManager.SaveScene(_harness.Scope.Scene, scenePath);
            TsLinkedScene.ClearOverride();
            TsLinkedScene.ScenePath = scenePath;

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            CollectionAssert.IsNotEmpty(GeneratedCsFiles(),
                "A real pass against the linked, loaded scene must write generated .cs files as usual.");
        }

        private static string[] GeneratedCsFiles() =>
            Directory.Exists(TsPaths.GeneratedFolder) ? Directory.GetFiles(TsPaths.GeneratedFolder, "*.cs") : new string[0];
    }
}
