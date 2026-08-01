using System.IO;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Core.Generated;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Proves the shared harness (TempSceneScope + TsPaths + CompiledRootFixture +
    // TsGeneratorTestHarness + PrivateFieldAccess + ScratchAssets) actually works end to end.
    // Every later Wire()/Run()-level test builds on these same pieces.
    public class HarnessProofTests
    {
        [Test]
        public void MemoryModule_Wire_OnFreshTempSceneWithNoTsMemory_DoesNotThrowAndTouchesNoExtraObjects()
        {
            using var scope = new TempSceneScope();
            CompiledRootFixture.AddTo(scope);

            Assert.DoesNotThrow(() => new MemoryModule().Wire());

            Assert.AreEqual(1, scope.Scene.rootCount, "Wire() with no TsvrcMemory in scene must not create or destroy any scene objects.");
        }

        [Test]
        public void ScratchAssets_EnsureFolderThenDeleteAll_RoundTripsCleanly()
        {
            ScratchAssets.EnsureFolder();
            Assert.IsTrue(AssetDatabase.IsValidFolder(ScratchAssets.Folder));

            var assetPath = ScratchAssets.Folder + "/DummyProofAsset.asset";
            var dummy = ScriptableObject.CreateInstance<TsBuiltinConfig>();
            AssetDatabase.CreateAsset(dummy, assetPath);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>(assetPath));

            ScratchAssets.DeleteAll();
            Assert.IsFalse(AssetDatabase.IsValidFolder(ScratchAssets.Folder));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<TsBuiltinConfig>(assetPath));
        }

        [Test]
        public void ScratchAssets_DeleteAll_FolderExistsOnDiskButAssetDatabaseNeverLearnedAboutIt_DeletesItAnyway()
        {
            // Reproduces the actual bug this fallback exists for: ModuleEntrySnapshot.Save
            // writes via raw Directory.CreateDirectory/File.WriteAllText, never calling
            // AssetDatabase.Refresh(), so AssetDatabase.IsValidFolder never becomes true even
            // though real files sit on disk - the exact condition that used to leave the
            // __Scratch__ folder behind indefinitely.
            Directory.CreateDirectory(ScratchAssets.Folder + "/RawlyCreated");
            File.WriteAllText(ScratchAssets.Folder + "/RawlyCreated/debris.txt", "debris");
            Assert.IsFalse(AssetDatabase.IsValidFolder(ScratchAssets.Folder),
                "Precondition: AssetDatabase must not know about a raw-created folder.");

            ScratchAssets.DeleteAll();

            Assert.IsFalse(Directory.Exists(ScratchAssets.Folder),
                "DeleteAll() must remove the folder from disk even when AssetDatabase never learned about it.");
        }

        [Test]
        public void PrivateFieldAccess_SetAndGetField_RoundTrips()
        {
            var pool = new PoolModule();
            var entries = new System.Collections.Generic.List<(UnityEngine.Component, string)>();
            PrivateFieldAccess.SetField(pool, "_poolEntries", entries);
            Assert.AreSame(entries, PrivateFieldAccess.GetField<object>(pool, "_poolEntries"));
        }

        [Test]
        public void CompiledRootFixture_AddTo_ResolvesViaFindCompiledType_NotJustTheDirectReference()
        {
            // The point of the fixture isn't just that a TestGenerated instance exists. It's
            // that production code's own lookup path, ScaffoldModule.FindCompiledType() calling
            // FindObjectOfType, finds it too, exactly as Wire() and FindRoot() rely on.
            using var scope = new TempSceneScope();
            var added = CompiledRootFixture.AddTo(scope);

            var compiledType = ScaffoldModule.FindCompiledType();
            Assert.IsNotNull(compiledType, "FindCompiledType() must resolve once CompiledRootFixture redirects TsPaths.CompiledClassName.");
            Assert.AreEqual(typeof(TestGenerated), compiledType);
            Assert.AreSame(added, Object.FindObjectOfType(compiledType, true));
        }

        [Test]
        public void TempSceneScope_Dispose_ResetsTsPathsRedirectsMadeDuringTheScope()
        {
            var scope = new TempSceneScope();
            CompiledRootFixture.AddTo(scope); // redirects TsPaths.CompiledClassName
            TsPaths.GeneratedFolder = "Assets/SomeOtherFolder/NotReal";

            scope.Dispose();

            Assert.AreEqual(TsPaths.DefaultCompiledClassName, TsPaths.CompiledClassName,
                "A test's TsPaths redirects must never leak into the next test.");
            Assert.AreEqual(TsPaths.DefaultGeneratedFolder, TsPaths.GeneratedFolder);
        }

        [Test]
        public void TsGeneratorTestHarness_Run_NeverWritesUnderTheRealGeneratedFolder()
        {
            using var harness = new TsGeneratorTestHarness();
            harness.CreateGameObject("TsConfig").AddComponent<TsConfig>();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.AreNotEqual(TsPaths.DefaultGeneratedFolder, TsPaths.GeneratedFolder,
                "The harness must redirect GeneratedFolder away from the real project path before Run() ever executes.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(TsPaths.DefaultGeneratedFolder + "/__should_never_exist__"));
        }

        [Test]
        public void TsGeneratorTestHarness_Dispose_DeletesItsScratchFolder()
        {
            string scratchGeneratedFolder;
            using (var harness = new TsGeneratorTestHarness())
            {
                harness.CreateGameObject("TsConfig").AddComponent<TsConfig>();
                TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
                scratchGeneratedFolder = TsPaths.GeneratedFolder;
                Assert.IsTrue(AssetDatabase.IsValidFolder(ScratchAssets.Folder), "Run() must have written something under the scratch root.");
            }

            Assert.IsFalse(AssetDatabase.IsValidFolder(scratchGeneratedFolder), "Dispose() must leave no scratch-written generated files behind.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(ScratchAssets.Folder), "Dispose() must leave no trace under the scratch root at all.");
        }
    }
}
