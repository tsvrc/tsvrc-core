using NUnit.Framework;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Proves the shared harness (TempSceneScope + CompiledRootFixture + PrivateFieldAccess
    // + ScratchAssets) actually works end to end, per CODEGEN_TESTING_PLAN.md Phase G0.
    // Every later Wire()-level test builds on these same pieces.
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
            PrivateFieldAccess.SetField(pool, "_hasAnyConfigured", true);
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(pool, "_hasAnyConfigured"));
        }
    }
}
