using System.IO;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

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

        // ScenePath persists as the linked scene's GUID (see TsLinkedSceneConfig), which requires
        // a real SceneAsset to resolve from - saves the harness's scene to scratch, links it,
        // then swaps the active scene away so it's genuinely unloaded rather than merely unsaved.
        // Shared by every test below that needs the "configured but not loaded" state, so the
        // real-scene setup dance lives in exactly one place.
        private string LinkRealSceneButLeaveItUnloaded([System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
        {
            TsLinkedScene.ClearOverride();
            string linkedPath = $"{ScratchAssets.Folder}/{callerName}Linked.unity";
            EditorSceneManager.SaveScene(_harness.Scope.Scene, linkedPath);
            TsLinkedScene.ScenePath = linkedPath;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return linkedPath;
        }

        [Test]
        public void Run_LinkedSceneConfiguredButNotLoaded_WritesNothingAndDoesNotThrow()
        {
            LinkRealSceneButLeaveItUnloaded();
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

        [Test]
        public void ManualGenerate_LinkedSceneConfiguredButNotLoaded_LogsWarningAndWritesNothing()
        {
            LinkRealSceneButLeaveItUnloaded();

            LogAssert.Expect(LogType.Warning,
                $"[Tsvrc] Linked scene '{TsLinkedScene.ScenePath}' is not open - nothing to regenerate. Open it first.");
            TsGenerator.ManualGenerate();

            CollectionAssert.IsEmpty(GeneratedCsFiles(),
                "ManualGenerate() must not write anything when the linked scene isn't loaded.");
        }

        // ValidateManualGenerate is private (it's a MenuItem validate function, never called
        // directly by production code), so reached via reflection here - the same pattern used
        // elsewhere in this suite for other MenuItem validate functions.
        [Test]
        public void ValidateManualGenerate_LinkedSceneConfiguredButNotLoaded_ReturnsFalse()
        {
            LinkRealSceneButLeaveItUnloaded();

            var method = typeof(TsGenerator).GetMethod("ValidateManualGenerate", BindingFlags.NonPublic | BindingFlags.Static);
            bool result = (bool)method.Invoke(null, null);

            Assert.IsFalse(result, "The menu item must be greyed out while the linked scene isn't open.");
        }
    }
}
