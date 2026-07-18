using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.UI;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class TsListItemTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private TsListItemTestSubclass CreateItem()
        {
            var go = new GameObject(nameof(TsListItem));
            _spawned.Add(go);
            return go.AddComponent<TsListItemTestSubclass>();
        }

        private TsList CreateListDouble()
        {
            var go = new GameObject(nameof(TsList));
            _spawned.Add(go);
            return go.AddComponent<TsList>();
        }

        private static DataDictionary MakeDict(string key, string value)
        {
            var dict = new DataDictionary();
            dict.Add(key, new DataToken(value));
            return dict;
        }

        [Test]
        public void Bind_SetsBoundStateAndDataIndex()
        {
            TsListItemTestSubclass item = CreateItem();
            TsList list = CreateListDouble();
            DataDictionary data = MakeDict("k", "v");

            item.Bind(list, 3, data);

            Assert.IsTrue(item.IsBound);
            Assert.AreEqual(3, item.DataIndex);
            Assert.AreSame(list, item.ListField);
        }

        [Test]
        public void Bind_InvokesOnBindExactlyOnceWithCorrectData()
        {
            TsListItemTestSubclass item = CreateItem();
            TsList list = CreateListDouble();
            DataDictionary data = MakeDict("k", "v");

            item.Bind(list, 3, data);

            Assert.AreEqual(1, item.OnBindCallCount);
            Assert.AreSame(data, item.DataAtLastBind);
            Assert.AreEqual(3, item.DataIndexAtLastBind);
        }

        [Test]
        public void Bind_WithNullData_DoesNotThrowAndStoresNullData()
        {
            TsListItemTestSubclass item = CreateItem();
            TsList list = CreateListDouble();

            Assert.DoesNotThrow(() => item.Bind(list, 0, null));

            Assert.IsTrue(item.IsBound);
            Assert.IsNull(item.DataAtLastBind);
        }

        [Test]
        public void Bind_CalledTwiceWithoutUnbind_OverwritesStateAndInvokesOnBindTwiceWithoutOnUnbind()
        {
            // Documents current behavior: Bind does not guard against being called again
            // while already bound (production usage never hits this — TsList always
            // destroys and re-instantiates items rather than re-binding one in place).
            TsListItemTestSubclass item = CreateItem();
            TsList list = CreateListDouble();
            item.Bind(list, 1, MakeDict("k", "first"));

            item.Bind(list, 2, MakeDict("k", "second"));

            Assert.AreEqual(2, item.OnBindCallCount);
            Assert.AreEqual(0, item.OnUnbindCallCount, "_OnUnbind is not called by a second Bind.");
            Assert.AreEqual(2, item.DataIndex);
            Assert.AreEqual("second", item.DataAtLastBind["k"].String);
        }

        [Test]
        public void Unbind_ClearsBoundStateAndDataIndex()
        {
            TsListItemTestSubclass item = CreateItem();
            TsList list = CreateListDouble();
            item.Bind(list, 3, MakeDict("k", "v"));

            item.Unbind();

            Assert.IsFalse(item.IsBound);
            Assert.AreEqual(-1, item.DataIndex);
        }

        [Test]
        public void Unbind_InvokesOnUnbindExactlyOnce()
        {
            TsListItemTestSubclass item = CreateItem();
            TsList list = CreateListDouble();
            item.Bind(list, 3, MakeDict("k", "v"));

            item.Unbind();

            Assert.AreEqual(1, item.OnUnbindCallCount);
        }

        [Test]
        public void Unbind_WithoutPriorBind_DoesNotThrowAndStillInvokesOnUnbind()
        {
            TsListItemTestSubclass item = CreateItem();

            Assert.DoesNotThrow(() => item.Unbind());
            Assert.AreEqual(1, item.OnUnbindCallCount);
        }

        [Test]
        public void OnItemPressed_WhenBound_NotifiesOwningListWithDataIndex()
        {
            TsListItemTestSubclass item = CreateItem();
            TsList list = CreateListDouble();
            list.SetData(MakeList(3));
            item.Bind(list, 2, MakeDict("k", "v"));

            item._OnItemPressed();

            Assert.AreEqual(2, list.SelectedIndex);
        }

        [Test]
        public void OnItemPressed_WhenNotBound_DoesNotThrowAndDoesNotNotifyList()
        {
            TsListItemTestSubclass item = CreateItem();
            TsList list = CreateListDouble();
            list.SetData(MakeList(3));
            item.Bind(list, 2, MakeDict("k", "v"));
            item.Unbind();

            Assert.DoesNotThrow(() => item._OnItemPressed());
            Assert.AreEqual(-1, list.SelectedIndex);
        }

        [Test]
        public void OnItemPressed_WithNoList_DoesNotThrow()
        {
            TsListItemTestSubclass item = CreateItem();

            Assert.DoesNotThrow(() => item._OnItemPressed());
        }

        private static DataList MakeList(int count)
        {
            var data = new DataList();
            for (int i = 0; i < count; i++)
                data.Add(new DataToken(MakeDict("index", i.ToString())));
            return data;
        }
    }
}
