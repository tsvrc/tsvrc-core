using System.Collections;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // FactoryModule.BuildEntries() via reflection with synthetic TsvrcConfig/
    // TsvrcBuiltinConfig instances (ScriptableObject.CreateInstance - never touching the
    // real AssetDatabase-backed builtin config).
    public class FactoryModuleBuildEntriesTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/FactoryBuildEntriesPrefab.prefab";

        private TempSceneScope _scope;
        private TsvrcConfig _userConfig;
        private TsvrcBuiltinConfig _builtinConfig;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _userConfig = _scope.CreateGameObject("Config").AddComponent<TsvrcConfig>();
            _builtinConfig = ScriptableObject.CreateInstance<TsvrcBuiltinConfig>();
            ScratchAssets.EnsureFolder();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            Object.DestroyImmediate(_builtinConfig);
            ScratchAssets.DeleteAll();
        }

        private static IList BuildEntries(TsvrcConfig userConfig, TsvrcBuiltinConfig builtinConfig)
            => (IList)PrivateFieldAccess.InvokeStatic(typeof(FactoryModule), "BuildEntries", userConfig, builtinConfig);

        private static string NameOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "Name");

        private GameObject CreateScratchPrefab(string name)
        {
            var go = _scope.CreateGameObject(name);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath.Replace("FactoryBuildEntriesPrefab", name));
            return prefab;
        }

        [Test]
        public void BuildEntries_SceneObjectDraggedInAsPrefab_WarnsAndSkips()
        {
            var sceneGo = _scope.CreateGameObject("NotAPrefab");
            _userConfig.Factories = new[] { new TsvrcFactoryGroup { GroupName = "", Prefabs = new Object[] { sceneGo } } };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*is a scene object, not a prefab asset.*"));

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void BuildEntries_NullEntryInPrefabsArray_SilentlySkipped()
        {
            _userConfig.Factories = new[] { new TsvrcFactoryGroup { GroupName = "", Prefabs = new Object[] { null } } };

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void BuildEntries_GroupWithNullPrefabsArray_DoesNotThrow()
        {
            _userConfig.Factories = new[] { new TsvrcFactoryGroup { GroupName = "Foo", Prefabs = null } };

            Assert.DoesNotThrow(() => BuildEntries(_userConfig, null));
        }

        [Test]
        public void BuildEntries_ComponentDraggedIn_ResolvesToRootGameObjectSameAsGameObjectDragged()
        {
            var prefab = CreateScratchPrefab("CompVsGo");
            var byGameObject = new TsvrcFactoryGroup { GroupName = "", Prefabs = new Object[] { prefab } };
            var byComponent = new TsvrcFactoryGroup { GroupName = "", Prefabs = new Object[] { prefab.transform } };

            _userConfig.Factories = new[] { byGameObject };
            var resultGo = BuildEntries(_userConfig, null);

            _userConfig.Factories = new[] { byComponent };
            var resultComp = BuildEntries(_userConfig, null);

            Assert.AreEqual(NameOf(resultGo[0]), NameOf(resultComp[0]));
        }

        [Test]
        public void BuildEntries_TwoGroupsProduceSameFinalNameAfterSanitization_DedupedAcrossGroups()
        {
            var prefabA = CreateScratchPrefab("Shared");
            var prefabB = CreateScratchPrefab("Shared2");
            // "Foo" + "Shared" and "Foo " + "Shared" both sanitize to "FooShared".
            var groupA = new TsvrcFactoryGroup { GroupName = "Foo", Prefabs = new Object[] { prefabA } };
            var groupB = new TsvrcFactoryGroup { GroupName = "Foo", Prefabs = new Object[] { prefabA } };
            _userConfig.Factories = new[] { groupA, groupB };

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("FooShared", NameOf(result[0]));
            Assert.AreEqual("FooShared2", NameOf(result[1]), "Second group's collision must be deduplicated against the first.");
        }

        [Test]
        public void BuildEntries_BuiltinAndUserBothConfigured_BuiltinGroupsComeFirst()
        {
            var builtinPrefab = CreateScratchPrefab("BuiltinOne");
            var userPrefab = CreateScratchPrefab("UserOne");
            _builtinConfig.Factories = new[] { new TsvrcFactoryGroup { GroupName = "", Prefabs = new Object[] { builtinPrefab } } };
            _userConfig.Factories = new[] { new TsvrcFactoryGroup { GroupName = "", Prefabs = new Object[] { userPrefab } } };

            var result = BuildEntries(_userConfig, _builtinConfig);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("BuiltinOne", NameOf(result[0]));
            Assert.AreEqual("UserOne", NameOf(result[1]));
        }
    }
}
