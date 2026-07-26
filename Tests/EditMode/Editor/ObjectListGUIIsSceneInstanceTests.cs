using NUnit.Framework;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // ObjectListGUI.IsSceneInstance - internal static and pure (aside from reading
    // EditorUtility.IsPersistent), called directly. Reuses the ScratchAssets/
    // PrefabUtility.SaveAsPrefabAsset pattern already established in
    // PoolModuleResolveConfigTests.cs for creating a real, persisted asset to test against.
    public class ObjectListGUIIsSceneInstanceTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/ObjectListGUIIsSceneInstanceTests.prefab";

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            ScratchAssets.DeleteAll();
        }

        [Test]
        public void IsSceneInstance_Null_ReturnsFalse()
        {
            Assert.IsFalse(ObjectListGUI.IsSceneInstance(null));
        }

        [Test]
        public void IsSceneInstance_SceneGameObject_ReturnsTrue()
        {
            var go = _scope.CreateGameObject("SceneInstance");

            Assert.IsTrue(ObjectListGUI.IsSceneInstance(go));
        }

        [Test]
        public void IsSceneInstance_PrefabAsset_ReturnsFalse()
        {
            var go = _scope.CreateGameObject("PrefabSource");
            var prefabGo = PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath);

            Assert.IsFalse(ObjectListGUI.IsSceneInstance(prefabGo));
        }
    }
}
