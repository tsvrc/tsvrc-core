using NUnit.Framework;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Proves the shared harness (TempSceneScope + CompiledRootFixture + PrivateFieldAccess
    // + ScratchAssets) actually works end to end. Every later Wire()-level test builds on
    // these same pieces.
    public class HarnessProofTests
    {
        [Test]
        public void MemoryModule_Wire_OnFreshTempSceneWithNoTsMemory_DoesNotThrowAndTouchesNoExtraObjects()
        {
            using var scope = new TempSceneScope();
            CompiledRootFixture.AddTo(scope);

            Assert.DoesNotThrow(() => new MemoryModule().Wire());

            Assert.AreEqual(1, scope.Scene.rootCount, "Wire() with no TsMemory in scene must not create or destroy any scene objects.");
        }

        [Test]
        public void ScratchAssets_EnsureFolderThenDeleteAll_RoundTripsCleanly()
        {
            ScratchAssets.EnsureFolder();
            Assert.IsTrue(AssetDatabase.IsValidFolder(ScratchAssets.Folder));

            var assetPath = ScratchAssets.Folder + "/DummyProofAsset.asset";
            var dummy = ScriptableObject.CreateInstance<TsvrcBuiltinConfig>();
            AssetDatabase.CreateAsset(dummy, assetPath);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TsvrcBuiltinConfig>(assetPath));

            ScratchAssets.DeleteAll();
            Assert.IsFalse(AssetDatabase.IsValidFolder(ScratchAssets.Folder));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<TsvrcBuiltinConfig>(assetPath));
        }

        [Test]
        public void PrivateFieldAccess_SetAndGetField_RoundTrips()
        {
            var pool = new PoolModule();
            var entries = new System.Collections.Generic.List<(UnityEngine.Component, string)>();
            PrivateFieldAccess.SetField(pool, "_poolEntries", entries);
            Assert.AreSame(entries, PrivateFieldAccess.GetField<object>(pool, "_poolEntries"));
        }
    }
}
