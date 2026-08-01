using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // FactoryModule.ComputeNameCollisions(), the helper behind DrawTab's inline "renamed due to a
    // name collision" hint. Mirrors BuildEntries' own Sanitize+Deduplicate step so the Configure
    // window can flag a same-module collision before a regenerate, not just discover the renamed
    // result afterward in generated code.
    public class FactoryModuleNameCollisionHintTests
    {
        private const string ScratchPrefabPath = ScratchAssets.Folder + "/FactoryCollisionHintPrefab.prefab";

        private TempSceneScope _scope;
        private TsConfig _config;
        private SerializedObject _so;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _config = _scope.CreateGameObject("Config").AddComponent<TsConfig>();
            ScratchAssets.EnsureFolder();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            ScratchAssets.DeleteAll();
        }

        private GameObject CreateScratchPrefab(string name)
        {
            var go = _scope.CreateGameObject(name);
            return PrefabUtility.SaveAsPrefabAsset(go, ScratchPrefabPath.Replace("FactoryCollisionHintPrefab", name));
        }

        private static List<string> ComputeNameCollisions(SerializedProperty prefabsProp, string prefix, HashSet<string> usedNames)
            => (List<string>)PrivateFieldAccess.InvokeStatic(typeof(FactoryModule), "ComputeNameCollisions", prefabsProp, prefix, usedNames);

        private SerializedProperty PrefabsPropertyFor(params Object[] prefabs)
        {
            _config.Factories = new[] { new TsFactoryGroup { GroupName = "", Prefabs = prefabs } };
            _so = new SerializedObject(_config);
            return _so.FindProperty("Factories").GetArrayElementAtIndex(0).FindPropertyRelative("Prefabs");
        }

        [Test]
        public void NoCollisions_ReturnsEmptyList()
        {
            var a = CreateScratchPrefab("Alpha");
            var b = CreateScratchPrefab("Beta");
            var prop = PrefabsPropertyFor(a, b);

            var collisions = ComputeNameCollisions(prop, "", new HashSet<string>());

            Assert.IsEmpty(collisions);
        }

        [Test]
        public void TwoPrefabsSanitizeToSameName_ReturnsOneCollisionNamingBoth()
        {
            // "FooBar" and "Foo Bar" both sanitize to "FooBar" (Sanitize splits on non-
            // alphanumeric separators and PascalCases each word).
            var a = CreateScratchPrefab("FooBar");
            var b = CreateScratchPrefab("Foo Bar");
            var prop = PrefabsPropertyFor(a, b);

            var collisions = ComputeNameCollisions(prop, "", new HashSet<string>());

            Assert.AreEqual(1, collisions.Count);
            StringAssert.Contains("FooBar", collisions[0]);
        }

        [Test]
        public void NullEntry_IsSkippedWithoutThrowing()
        {
            var a = CreateScratchPrefab("Alpha");
            var prop = PrefabsPropertyFor(a, null);

            Assert.DoesNotThrow(() => ComputeNameCollisions(prop, "", new HashSet<string>()));
        }

        [Test]
        public void PreSeededUsedNames_CollisionAgainstAnEarlierGroupIsDetected()
        {
            // Simulates DrawTab's own usedNames set carrying forward across groups rendered in
            // the same pass: a name already used by an earlier group still collides here, even
            // though this group's own prefab list has no internal collision.
            var a = CreateScratchPrefab("Shared");
            var prop = PrefabsPropertyFor(a);
            var usedNames = new HashSet<string> { "Shared" };

            var collisions = ComputeNameCollisions(prop, "", usedNames);

            Assert.AreEqual(1, collisions.Count);
        }

        [Test]
        public void GroupPrefix_IsIncludedInTheCollisionCheck()
        {
            var a = CreateScratchPrefab("Bullet");
            var prop = PrefabsPropertyFor(a);
            var usedNames = new HashSet<string> { "MazeBullet" };

            var collisions = ComputeNameCollisions(prop, "Maze", usedNames);

            Assert.AreEqual(1, collisions.Count, "The prefix must be applied before comparing against already-used names.");
        }
    }
}
