using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Utils;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    public class TsArrayTests
    {
        private class DummyBehaviour : UdonSharpBehaviour
        {
        }

        private readonly List<GameObject> _spawnedGameObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawnedGameObjects)
                Object.DestroyImmediate(go);

            _spawnedGameObjects.Clear();
        }

        private UdonSharpBehaviour[] CreateBehaviours(int count)
        {
            UdonSharpBehaviour[] behaviours = new UdonSharpBehaviour[count];
            for (int i = 0; i < count; i++)
            {
                GameObject go = new GameObject($"DummyBehaviour_{i}");
                _spawnedGameObjects.Add(go);
                behaviours[i] = go.AddComponent<DummyBehaviour>();
            }

            return behaviours;
        }

        [Test]
        public void Add_ConcatenatesInOrder()
        {
            string[] result = TsArray.Add(new[] { "a", "b" }, new[] { "c", "d" });

            Assert.AreEqual(new[] { "a", "b", "c", "d" }, result);
        }

        [Test]
        public void Add_WithEmptyItems_ReturnsCopyOfOriginal()
        {
            string[] original = { "a", "b" };

            string[] result = TsArray.Add(original, new string[0]);

            Assert.AreEqual(original, result);
        }

        [Test]
        public void Remove_DropsAllMatchingOccurrences()
        {
            string[] result = TsArray.Remove(new[] { "a", "b", "a", "c" }, new[] { "a" });

            Assert.AreEqual(new[] { "b", "c" }, result);
        }

        [Test]
        public void Remove_WithNoMatches_ReturnsAllOriginalElements()
        {
            string[] original = { "a", "b", "c" };

            string[] result = TsArray.Remove(original, new[] { "z" });

            Assert.AreEqual(original, result);
        }

        [Test]
        public void Contains_FindsExistingValue()
        {
            Assert.IsTrue(TsArray.Contains(new[] { "a", "b", "c" }, "b"));
        }

        [Test]
        public void Contains_ReturnsFalseForMissingValue()
        {
            Assert.IsFalse(TsArray.Contains(new[] { "a", "b", "c" }, "z"));
        }

        [Test]
        public void Add_Behaviours_ConcatenatesInOrder()
        {
            UdonSharpBehaviour[] original = CreateBehaviours(2);
            UdonSharpBehaviour[] items = CreateBehaviours(1);

            UdonSharpBehaviour[] result = TsArray.Add(original, items);

            Assert.AreEqual(3, result.Length);
            Assert.AreEqual(original[0], result[0]);
            Assert.AreEqual(original[1], result[1]);
            Assert.AreEqual(items[0], result[2]);
        }

        [Test]
        public void Remove_Behaviours_DropsMatchingOccurrences()
        {
            UdonSharpBehaviour[] all = CreateBehaviours(3);
            UdonSharpBehaviour[] toRemove = { all[1] };

            UdonSharpBehaviour[] result = TsArray.Remove(all, toRemove);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual(all[0], result[0]);
            Assert.AreEqual(all[2], result[1]);
        }

        [Test]
        public void Contains_Behaviours_FindsExistingValue()
        {
            UdonSharpBehaviour[] all = CreateBehaviours(2);

            Assert.IsTrue(TsArray.Contains(all, all[1]));
        }

        [Test]
        public void Contains_Behaviours_ReturnsFalseForMissingValue()
        {
            UdonSharpBehaviour[] all = CreateBehaviours(2);
            UdonSharpBehaviour[] other = CreateBehaviours(1);

            Assert.IsFalse(TsArray.Contains(all, other[0]));
        }
    }
}
