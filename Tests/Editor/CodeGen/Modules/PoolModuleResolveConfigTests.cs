using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // PoolModule.ResolveConfig() + the builtin/user dedup-by-type-name step from
    // LoadConfig(), via reflection with synthetic prefab assets. Phase G3.4. The tuple
    // element types (Component, string) are both public, so the reflection Invoke result
    // can be cast directly to List<(Component, string)> without needing to reach into a
    // private nested type.
    public class PoolModuleResolveConfigTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/PoolResolveConfigPrefab.prefab";

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

        private static List<(Component prefab, string typeName)> ResolveConfig(TsvrcConfig userConfig, TsvrcBuiltinConfig builtinConfig)
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
            _builtinConfig.PoolPrefabs = new UdonSharp.UdonSharpBehaviour[] { sceneInstance };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*is a scene object.*"));

            var result = ResolveConfig(null, _builtinConfig);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ResolveConfig_UserNonPersistentEntry_WarnsAndSkips()
        {
            var sceneInstance = _scope.CreateGameObject("SceneOnly").AddComponent<StateManager>();
            _userConfig.PooledObjects = new UdonSharp.UdonSharpBehaviour[] { sceneInstance };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*is a scene object.*"));

            var result = ResolveConfig(_userConfig, null);

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void ResolveConfig_BuiltinEntriesPrecedeUserEntries()
        {
            var builtinPrefab = CreateScratchPrefab("BuiltinPrefab");
            var userPrefab = CreateScratchPrefab("UserPrefab");
            _builtinConfig.PoolPrefabs = new UdonSharp.UdonSharpBehaviour[] { builtinPrefab };
            _userConfig.PooledObjects = new UdonSharp.UdonSharpBehaviour[] { userPrefab };

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
            _builtinConfig.PoolPrefabs = new UdonSharp.UdonSharpBehaviour[] { builtinPrefab };
            _userConfig.PooledObjects = new UdonSharp.UdonSharpBehaviour[] { userPrefab };

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
