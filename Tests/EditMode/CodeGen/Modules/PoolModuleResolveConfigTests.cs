using NUnit.Framework;
using System.Collections.Generic;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using Tsvrc.Testing.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // PoolModule.ResolveConfig() + the builtin/user dedup-by-type-name step from
    // LoadConfig(), via reflection with synthetic prefab assets. The tuple element types
    // (Component, string) are both public, so the reflection Invoke result can be cast
    // directly to List<(Component, string)> without needing to reach into a private nested
    // type.
    public class PoolModuleResolveConfigTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/PoolResolveConfigPrefab.prefab";

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

        private static List<(Component prefab, string typeName)> ResolveConfig(TsConfig userConfig, TsBuiltinConfig builtinConfig)
            => (List<(Component, string)>)PrivateFieldAccess.InvokeStatic(typeof(PoolModule), "ResolveConfig", userConfig, builtinConfig);

        private StateManager CreateScratchPrefab(string name)
        {
            var go = _scope.CreateGameObject(name);
            go.AddComponent<StateManager>();
            var prefabGo = PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath.Replace("PoolResolveConfigPrefab", name));
            return prefabGo.GetComponent<StateManager>();
        }

        [Test]
        public void ResolveConfig_BuiltinNonPersistentEntry_WarnsAndSkips()
        {
            var sceneInstance = _scope.CreateGameObject("SceneOnly").AddComponent<StateManager>();
            _builtinConfig.PoolEntries = new[] { new TsGroupedEntry { Value = sceneInstance, GroupId = 0 } };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*is a scene object.*"));

            var result = ResolveConfig(null, _builtinConfig);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ResolveConfig_UserNonPersistentEntry_WarnsAndSkips()
        {
            var sceneInstance = _scope.CreateGameObject("SceneOnly").AddComponent<StateManager>();
            _userConfig.PoolEntries = new[] { new TsGroupedEntry { Value = sceneInstance, GroupId = 0 } };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*is a scene object.*"));

            var result = ResolveConfig(_userConfig, null);

            Assert.AreEqual(0, result.Count);
        }

        // A deleted prefab reference (a null slot) and a prefab dragged in twice both route
        // through TsModule.TryAcceptEntry, exactly like GlobalModule/ConstructModule.
        [Test]
        public void ResolveConfig_UserNullEntry_LogsWarningAndSkips()
        {
            _userConfig.PoolEntries = new[] { new TsGroupedEntry { Value = null, GroupId = 0 } };

            LogAssert.Expect(LogType.Warning, "[PoolModule] Null entry in config, remove the missing-script slot.");
            var result = ResolveConfig(_userConfig, null);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ResolveConfig_BuiltinNullEntry_LogsWarningAndSkips()
        {
            _builtinConfig.PoolEntries = new[] { new TsGroupedEntry { Value = null, GroupId = 0 } };

            LogAssert.Expect(LogType.Warning, "[PoolModule] Null entry in config, remove the missing-script slot.");
            var result = ResolveConfig(null, _builtinConfig);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ResolveConfig_SamePrefabRegisteredTwiceInUserList_LogsDuplicateWarningAndSkipsSecondOccurrence()
        {
            var prefab = CreateScratchPrefab("DupUser");
            _userConfig.PoolEntries = new[] { new TsGroupedEntry { Value = prefab, GroupId = 0 }, new TsGroupedEntry { Value = prefab, GroupId = 0 } };

            LogAssert.Expect(LogType.Warning, $"[PoolModule] Duplicate pool prefab '{prefab.name}' in config, remove the duplicate.");
            var result = ResolveConfig(_userConfig, null);

            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void ResolveConfig_SamePrefabInBothBuiltinAndUser_LogsDuplicateWarning()
        {
            // Duplicate detection is reference-based and spans both lists (a shared `seen` set),
            // not just within one - registering the exact same prefab asset as both a builtin and
            // a user entry is exactly as much a mistake as registering it twice in one list.
            var prefab = CreateScratchPrefab("DupAcrossBoth");
            _builtinConfig.PoolEntries = new[] { new TsGroupedEntry { Value = prefab, GroupId = 0 } };
            _userConfig.PoolEntries = new[] { new TsGroupedEntry { Value = prefab, GroupId = 0 } };

            LogAssert.Expect(LogType.Warning, $"[PoolModule] Duplicate pool prefab '{prefab.name}' in config, remove the duplicate.");
            var result = ResolveConfig(_userConfig, _builtinConfig);

            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void ResolveConfig_BuiltinEntriesPrecedeUserEntries()
        {
            var builtinPrefab = CreateScratchPrefab("BuiltinPrefab");
            var userPrefab = CreateScratchPrefab("UserPrefab");
            _builtinConfig.PoolEntries = new[] { new TsGroupedEntry { Value = builtinPrefab, GroupId = 0 } };
            _userConfig.PoolEntries = new[] { new TsGroupedEntry { Value = userPrefab, GroupId = 0 } };

            var result = ResolveConfig(_userConfig, _builtinConfig);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(builtinPrefab, result[0].prefab);
            Assert.AreEqual(userPrefab, result[1].prefab);
        }

        [Test]
        public void SameTypeInBothBuiltinAndUser_LoadConfigDedupKeepsBuiltinOccurrence()
        {
            // Mirrors PoolModule.LoadConfig()'s own dedup step
            // (_poolEntries.Where(seenEntryTypes.Add(e.typeName))) applied to
            // ResolveConfig()'s combined output - since both prefabs are StateManager
            // (same typeName), the builtin one (first in iteration order) must win.
            var builtinPrefab = CreateScratchPrefab("BuiltinSM");
            var userPrefab = CreateScratchPrefab("UserSM");
            _builtinConfig.PoolEntries = new[] { new TsGroupedEntry { Value = builtinPrefab, GroupId = 0 } };
            _userConfig.PoolEntries = new[] { new TsGroupedEntry { Value = userPrefab, GroupId = 0 } };

            var combined = ResolveConfig(_userConfig, _builtinConfig);
            var seenTypes = new HashSet<string>();
            var deduped = new List<(Component prefab, string typeName)>();
            foreach (var entry in combined)
                if (seenTypes.Add(entry.typeName))
                    deduped.Add(entry);

            Assert.AreEqual(1, deduped.Count);
            Assert.AreEqual(builtinPrefab, deduped[0].prefab, "Builtin occurrence must survive the dedup, not the user one.");
        }
    }
}
