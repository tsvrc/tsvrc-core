using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.UI;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.PlayMode.UI.TsList
{
    // Destroy() does not actually run in Edit Mode, so real pooled ListItem GameObjects are
    // never actually destroyed there. This covers the real, deferred Instantiate/Destroy
    // pool-churn lifecycle a page change or data reset triggers.
    public class TsListPlayModeTests : TsPlayModeTestBase
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private Tsvrc.UI.TsList CreateList(int pageSize = -1)
        {
            var listGO = new GameObject(nameof(Tsvrc.UI.TsList));
            _spawned.Add(listGO);
            var list = listGO.AddComponent<Tsvrc.UI.TsList>();
            list.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);

            var containerGO = new GameObject("Container");
            containerGO.transform.SetParent(listGO.transform);
            _spawned.Add(containerGO);

            var prefabGO = new GameObject("ItemPrefab");
            _spawned.Add(prefabGO);
            var prefabItem = prefabGO.AddComponent<ListItemTestSubclass>();

            PrivateFieldAccess.SetField(list, "_itemContainer", containerGO.transform);
            PrivateFieldAccess.SetField(list, "_itemPrefab", prefabItem);
            PrivateFieldAccess.SetField(list, "_pageSize", pageSize);

            return list;
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

        private static ListItem[] GetPool(Tsvrc.UI.TsList list) =>
            PrivateFieldAccess.GetField<ListItem[]>(list, "_pool");

        [UnityTest]
        public IEnumerator SetPage_RealPageChange_ActuallyDestroysOldPooledItemsByEndOfFrame()
        {
            var list = CreateList(pageSize: 2);
            list.SetData(MakeData(4));

            ListItem[] firstPagePool = GetPool(list);
            Assert.AreEqual(2, firstPagePool.Length);
            var oldItemGameObjects = new GameObject[firstPagePool.Length];
            for (int i = 0; i < firstPagePool.Length; i++)
                oldItemGameObjects[i] = firstPagePool[i].gameObject;

            list.SetPage(1);
            yield return null;

            foreach (GameObject go in oldItemGameObjects)
                Assert.IsTrue(go == null,
                    "A page change must actually destroy the previous page's pooled items by end of frame.");
        }

        [UnityTest]
        public IEnumerator SetData_RealNullAfterPopulated_ActuallyDestroysPooledItems()
        {
            var list = CreateList();
            list.SetData(MakeData(3));
            ListItem[] pool = GetPool(list);
            var oldItemGameObjects = new GameObject[pool.Length];
            for (int i = 0; i < pool.Length; i++)
                oldItemGameObjects[i] = pool[i].gameObject;

            list.SetData(null);
            yield return null;

            foreach (GameObject go in oldItemGameObjects)
                Assert.IsTrue(go == null,
                    "Resetting to empty data must actually destroy the previously pooled items by end of frame.");
        }
    }
}
