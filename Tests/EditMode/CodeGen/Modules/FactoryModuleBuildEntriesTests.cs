using NUnit.Framework;
using System.Collections;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // FactoryModule.BuildEntries() via reflection with synthetic TsConfig/
    // TsBuiltinConfig instances (ScriptableObject.CreateInstance - never touching the
    // real AssetDatabase-backed builtin config).
    public class FactoryModuleBuildEntriesTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/FactoryBuildEntriesPrefab.prefab";

        private TempSceneScope _scope;
        private TsConfig _userConfig;
        private TsBuiltinConfig _builtinConfig;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _userConfig = _scope.CreateGameObject("Config").AddComponent<TsConfig>();
            _builtinConfig = ScriptableObject.CreateInstance<TsBuiltinConfig>();
            ScratchAssets.EnsureFolder();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            Object.DestroyImmediate(_builtinConfig);
            ScratchAssets.DeleteAll();
        }

        private static IList BuildEntries(TsConfig userConfig, TsBuiltinConfig builtinConfig)
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
            _userConfig.Factories = new[] { new TsFactoryGroup { GroupName = "", Prefabs = new Object[] { sceneGo } } };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*is a scene object, not a prefab asset.*"));

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(0, result.Count);
        }

        // A deleted prefab reference logs a warning, matching Singleton/Construct/Pool's wording.
        [Test]
        public void BuildEntries_NullEntryInPrefabsArray_LogsWarningAndSkips()
        {
            _userConfig.Factories = new[] { new TsFactoryGroup { GroupName = "", Prefabs = new Object[] { null } } };

            LogAssert.Expect(LogType.Warning, "[FactoryModule] Null entry in config, remove the missing-script slot.");
            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void BuildEntries_SamePrefabRegisteredInTwoDifferentGroups_BothProduceEntries()
        {
            // Unlike Pool, a duplicate prefab reference here is legitimate (e.g. the same bullet
            // prefab spawned by two differently-named factory groups), so this must never warn or
            // drop the second occurrence the way PoolModule.ResolveConfig now does.
            var prefab = CreateScratchPrefab("SharedAcrossGroups");
            _userConfig.Factories = new[]
            {
                new TsFactoryGroup { GroupName = "Maze", Prefabs = new Object[] { prefab } },
                new TsFactoryGroup { GroupName = "Boss", Prefabs = new Object[] { prefab } },
            };

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void BuildEntries_GroupWithNullPrefabsArray_DoesNotThrow()
        {
            _userConfig.Factories = new[] { new TsFactoryGroup { GroupName = "Foo", Prefabs = null } };

            Assert.DoesNotThrow(() => BuildEntries(_userConfig, null));
        }

        [Test]
        public void BuildEntries_ComponentDraggedIn_ResolvesToRootGameObjectSameAsGameObjectDragged()
        {
            var prefab = CreateScratchPrefab("CompVsGo");
            var byGameObject = new TsFactoryGroup { GroupName = "", Prefabs = new Object[] { prefab } };
            var byComponent = new TsFactoryGroup { GroupName = "", Prefabs = new Object[] { prefab.transform } };

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
            var groupA = new TsFactoryGroup { GroupName = "Foo", Prefabs = new Object[] { prefabA } };
            var groupB = new TsFactoryGroup { GroupName = "Foo", Prefabs = new Object[] { prefabA } };
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
            _builtinConfig.Factories = new[] { new TsFactoryGroup { GroupName = "", Prefabs = new Object[] { builtinPrefab } } };
            _userConfig.Factories = new[] { new TsFactoryGroup { GroupName = "", Prefabs = new Object[] { userPrefab } } };

            var result = BuildEntries(_userConfig, _builtinConfig);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("BuiltinOne", NameOf(result[0]));
            Assert.AreEqual("UserOne", NameOf(result[1]));
        }
    }
}
