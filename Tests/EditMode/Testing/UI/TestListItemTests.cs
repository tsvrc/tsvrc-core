using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Testing.UI;
using Tsvrc.UI;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode.Testing.UI
{
    // TestListItem itself, in isolation - Tsvrc.Tests.EditMode.ListItemTests already proves the
    // base ListItem.Bind/Unbind contract in depth using its own near-identical double; these
    // cases prove TestListItem's own recording behavior specifically, since it's the one any
    // consuming world's tests actually reach for (see Tsvrc.Testing.UI's README section).
    public class TestListItemTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private TestListItem CreateItem()
        {
            var go = new GameObject(nameof(TestListItem));
            _spawned.Add(go);
            return go.AddComponent<TestListItem>();
        }

        private TsvrcList CreateListDouble()
        {
            var go = new GameObject(nameof(TsvrcList));
            _spawned.Add(go);
            return go.AddComponent<TsvrcList>();
        }

        private static DataDictionary MakeDict(string key, string value)
        {
            var dict = new DataDictionary();
            dict.Add(key, new DataToken(value));
            return dict;
        }

        [Test]
        public void Bind_RecordsOnBindCallCountDataAndIndex()
        {
            var item = CreateItem();
            var list = CreateListDouble();
            var data = MakeDict("k", "v");

            item.Bind(list, 2, data);

            Assert.AreEqual(1, item.OnBindCallCount);
            Assert.AreSame(data, item.DataAtLastBind);
            Assert.AreEqual(2, item.DataIndexAtLastBind);
        }

        [Test]
        public void Unbind_RecordsOnUnbindCallCount()
        {
            var item = CreateItem();
            var list = CreateListDouble();
            item.Bind(list, 0, MakeDict("k", "v"));

            item.Unbind();

            Assert.AreEqual(1, item.OnUnbindCallCount);
        }

        [Test]
        public void ListField_AfterBind_ExposesTheBoundList()
        {
            var item = CreateItem();
            var list = CreateListDouble();

            item.Bind(list, 0, MakeDict("k", "v"));

            Assert.AreSame(list, item.ListField);
        }

        [Test]
        public void Bind_CalledTwice_AccumulatesOnBindCallCount()
        {
            var item = CreateItem();
            var list = CreateListDouble();

            item.Bind(list, 0, MakeDict("k", "v"));
            item.Bind(list, 1, MakeDict("k", "v2"));

            Assert.AreEqual(2, item.OnBindCallCount);
            Assert.AreEqual(1, item.DataIndexAtLastBind);
        }
    }
}
