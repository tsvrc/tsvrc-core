using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
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

        // Builds a one-level TsGroup/TsGroupedEntry tree from (groupName, prefabs) pairs and
        // assigns it directly to config.FactoryGroups/FactoryEntries, since BuildEntries is
        // exercised in isolation here rather than through LoadConfig().
        private static void SetFlatGroups(TsConfig config, params (string groupName, Object[] prefabs)[] groups)
        {
            BuildFlatGroups(groups, out var groupList, out var entryList, out int nextId);
            config.FactoryGroups = groupList;
            config.FactoryEntries = entryList;
            config.FactoryNextGroupId = nextId;
        }

        private static void SetFlatGroups(TsBuiltinConfig config, params (string groupName, Object[] prefabs)[] groups)
        {
            BuildFlatGroups(groups, out var groupList, out var entryList, out int nextId);
            config.FactoryGroups = groupList;
            config.FactoryEntries = entryList;
            config.FactoryNextGroupId = nextId;
        }

        private static void BuildFlatGroups((string groupName, Object[] prefabs)[] groups,
            out TsGroup[] resultGroups, out TsGroupedEntry[] resultEntries, out int nextId)
        {
            var groupList = new List<TsGroup>();
            var entryList = new List<TsGroupedEntry>();
            nextId = 1;
            foreach (var (groupName, prefabs) in groups)
            {
                int id = nextId++;
                groupList.Add(new TsGroup { Id = id, ParentId = 0, Name = groupName });
                foreach (var prefab in prefabs ?? System.Array.Empty<Object>())
                    entryList.Add(new TsGroupedEntry { Value = prefab, GroupId = id });
            }
            resultGroups = groupList.ToArray();
            resultEntries = entryList.ToArray();
        }

        [Test]
        public void BuildEntries_SceneObjectDraggedInAsPrefab_WarnsAndSkips()
        {
            var sceneGo = _scope.CreateGameObject("NotAPrefab");
            SetFlatGroups(_userConfig, ("", new Object[] { sceneGo }));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*is a scene object, not a prefab asset.*"));

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(0, result.Count);
        }

        // A deleted prefab reference logs a warning, matching Global/Construct/Pool's wording.
        [Test]
        public void BuildEntries_NullEntryInPrefabsArray_LogsWarningAndSkips()
        {
            SetFlatGroups(_userConfig, ("", new Object[] { null }));

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
            SetFlatGroups(_userConfig,
                ("Maze", new Object[] { prefab }),
                ("Boss", new Object[] { prefab }));

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(2, result.Count);
        }

        [Test]
        public void BuildEntries_ComponentDraggedIn_ResolvesToRootGameObjectSameAsGameObjectDragged()
        {
            var prefab = CreateScratchPrefab("CompVsGo");

            SetFlatGroups(_userConfig, ("", new Object[] { prefab }));
            var resultGo = BuildEntries(_userConfig, null);

            SetFlatGroups(_userConfig, ("", new Object[] { prefab.transform }));
            var resultComp = BuildEntries(_userConfig, null);

            Assert.AreEqual(NameOf(resultGo[0]), NameOf(resultComp[0]));
        }

        [Test]
        public void BuildEntries_TwoGroupsProduceSameFinalNameAfterSanitization_DedupedAcrossGroups()
        {
            var prefabA = CreateScratchPrefab("Shared");
            var prefabB = CreateScratchPrefab("Shared2");
            // "Foo" + "Shared" (from either group, same prefab) sanitizes to "FooShared" both times.
            SetFlatGroups(_userConfig,
                ("Foo", new Object[] { prefabA }),
                ("Foo", new Object[] { prefabA }));

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
            SetFlatGroups(_builtinConfig, ("", new Object[] { builtinPrefab }));
            SetFlatGroups(_userConfig, ("", new Object[] { userPrefab }));

            var result = BuildEntries(_userConfig, _builtinConfig);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("BuiltinOne", NameOf(result[0]));
            Assert.AreEqual("UserOne", NameOf(result[1]));
        }

        [Test]
        public void BuildEntries_NestedSubGroup_PrefixIsFullAncestorChain()
        {
            var prefab = CreateScratchPrefab("Dragon");
            _userConfig.FactoryGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Enemies" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Bosses" },
            };
            _userConfig.FactoryEntries = new[] { new TsGroupedEntry { Value = prefab, GroupId = 2 } };
            _userConfig.FactoryNextGroupId = 3;

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("EnemiesBossesDragon", NameOf(result[0]));
        }

        [Test]
        public void BuildEntries_SameEffectivePrefixFromDifferentNestingDepths_DedupedAcrossGroups()
        {
            // A root-level group named "Boss" and an unrelated nested chain (an unnamed root
            // group containing a "Boss" sub-group) both resolve to the same effective prefix,
            // "Boss" - Deduplicate must catch this collision even though the two nodes sit at
            // different depths in the tree and share no ancestor.
            var prefabA = CreateScratchPrefab("Sword");
            var prefabB = CreateScratchPrefab("Sword2");
            _userConfig.FactoryGroups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Boss" },
                new TsGroup { Id = 2, ParentId = 0, Name = "" },
                new TsGroup { Id = 3, ParentId = 2, Name = "Boss" },
            };
            _userConfig.FactoryEntries = new[]
            {
                new TsGroupedEntry { Value = prefabA, GroupId = 1 },
                new TsGroupedEntry { Value = prefabB, GroupId = 3 },
            };
            _userConfig.FactoryNextGroupId = 4;

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("BossSword", NameOf(result[0]));
            Assert.AreEqual("BossSword2", NameOf(result[1]), "The second group's collision (same 'Boss' prefix, different tree position) must still be deduplicated.");
        }

        [Test]
        public void BuildEntries_UngroupedEntry_HasNoPrefix()
        {
            var prefab = CreateScratchPrefab("Loose");
            _userConfig.FactoryGroups = System.Array.Empty<TsGroup>();
            _userConfig.FactoryEntries = new[] { new TsGroupedEntry { Value = prefab, GroupId = 0 } };

            var result = BuildEntries(_userConfig, null);

            Assert.AreEqual("Loose", NameOf(result[0]));
        }
    }
}
