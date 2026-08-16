using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Testing.UI;
using Tsvrc.UI;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode.Testing.UI
{
    public class TsvrcListTestBuilderTests
    {
        // Mirrors TsvrcList's protected STATE_* constants (not accessible across assemblies).
        private const int STATE_EMPTY = 1;
        private const int STATE_POPULATED = 2;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private static DataList MakeData(int count)
        {
            var data = new DataList();
            for (int i = 0; i < count; i++)
            {
                var dict = new DataDictionary();
                dict.Add("index", new DataToken((double)i));
                data.Add(new DataToken(dict));
            }
            return data;
        }

        [Test]
        public void Build_ReturnsAUsableTsvrcListComponent()
        {
            var list = TsvrcListTestBuilder.Build(_spawned);

            Assert.IsNotNull(list);
            Assert.IsInstanceOf<TsvrcList>(list);
        }

        [Test]
        public void Build_WiresItemContainerToAChildOfTheListGameObject()
        {
            var list = TsvrcListTestBuilder.Build(_spawned);

            var container = PrivateFieldAccess.GetField<Transform>(list, "_itemContainer");

            Assert.IsNotNull(container);
            Assert.AreSame(list.transform, container.parent);
        }

        [Test]
        public void Build_WiresItemPrefabToATestListItem()
        {
            var list = TsvrcListTestBuilder.Build(_spawned);

            var prefab = PrivateFieldAccess.GetField<ListItem>(list, "_itemPrefab");

            Assert.IsInstanceOf<TestListItem>(prefab);
        }

        [Test]
        public void Build_DefaultPageSize_IsUnpaginated()
        {
            var list = TsvrcListTestBuilder.Build(_spawned);

            Assert.AreEqual(-1, PrivateFieldAccess.GetField<int>(list, "_pageSize"));
        }

        [Test]
        public void Build_ExplicitPageSize_IsWiredThrough()
        {
            var list = TsvrcListTestBuilder.Build(_spawned, pageSize: 5);

            Assert.AreEqual(5, PrivateFieldAccess.GetField<int>(list, "_pageSize"));
        }

        [Test]
        public void Build_EveryCreatedGameObject_IsAppendedToTheCallersSpawnedList()
        {
            int before = _spawned.Count;

            TsvrcListTestBuilder.Build(_spawned);

            // List GameObject + Container + ItemPrefab, at minimum.
            Assert.GreaterOrEqual(_spawned.Count - before, 3);
            CollectionAssert.AllItemsAreNotNull(_spawned);
        }

        [Test]
        public void Build_ReturnedList_ActuallyPopulatesRealDataThroughSetData()
        {
            // End-to-end proof the wiring is real, not just present: SetData against a builder
            // list must reach STATE_POPULATED, exactly like a hand-wired TsvrcList would.
            var list = TsvrcListTestBuilder.Build(_spawned);

            list.SetData(MakeData(3));

            Assert.AreEqual(STATE_POPULATED, list.ListState);
        }

        [Test]
        public void Build_ReturnedList_SetDataEmptyReachesEmptyState()
        {
            var list = TsvrcListTestBuilder.Build(_spawned);

            list.SetData(MakeData(0));

            Assert.AreEqual(STATE_EMPTY, list.ListState);
        }

        [Test]
        public void Build_MultipleCalls_ProduceIndependentLists()
        {
            var first = TsvrcListTestBuilder.Build(_spawned);
            var second = TsvrcListTestBuilder.Build(_spawned);

            Assert.AreNotSame(first, second);
        }
    }
}
