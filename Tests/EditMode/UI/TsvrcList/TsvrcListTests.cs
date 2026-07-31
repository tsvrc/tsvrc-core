using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Tsvrc.Testing.Framework;
using Tsvrc.UI;
using UnityEngine.TestTools;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class TsListTests
    {
        private static readonly Regex DestroyEditModeRegex = new Regex("^Destroy may not be called from edit mode");

        // Mirrors TsvrcList's protected STATE_* constants (not accessible across assemblies).
        private const int STATE_LOADING = 0;
        private const int STATE_EMPTY = 1;
        private const int STATE_POPULATED = 2;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private void ExpectDestroyErrors(int count)
        {
            for (int i = 0; i < count; i++)
                LogAssert.Expect(LogType.Error, DestroyEditModeRegex);
        }

        private TsvrcList CreateList(int pageSize = -1, GameObject loadingIndicator = null, GameObject emptyIndicator = null)
        {
            var listGO = new GameObject(nameof(TsvrcList));
            _spawned.Add(listGO);
            var list = listGO.AddComponent<TsvrcList>();

            var containerGO = new GameObject("Container");
            containerGO.transform.SetParent(listGO.transform);
            _spawned.Add(containerGO);

            var prefabGO = new GameObject("ItemPrefab");
            _spawned.Add(prefabGO);
            var prefabItem = prefabGO.AddComponent<ListItemTestSubclass>();

            PrivateFieldAccess.SetField(list, "_itemContainer", containerGO.transform);
            PrivateFieldAccess.SetField(list, "_itemPrefab", prefabItem);
            PrivateFieldAccess.SetField(list, "_pageSize", pageSize);
            if (loadingIndicator != null) PrivateFieldAccess.SetField(list, "_loadingIndicator", loadingIndicator);
            if (emptyIndicator != null) PrivateFieldAccess.SetField(list, "_emptyIndicator", emptyIndicator);

            return list;
        }

        private GameObject CreateIndicator(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
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

        private static ListItem[] GetPool(TsvrcList list) => PrivateFieldAccess.GetField<ListItem[]>(list, "_pool");

        [Test]
        public void InitialState_BeforeAnySetData_IsLoadingWithNoSelectionOrPage()
        {
            TsvrcList list = CreateList();

            Assert.AreEqual(STATE_LOADING, list.ListState);
            Assert.AreEqual(-1, list.SelectedIndex);
            Assert.AreEqual(0, list.CurrentPage);
            Assert.AreEqual(0, GetPool(list).Length);
            Assert.IsFalse(list.HasNextPage);
            Assert.IsFalse(list.HasPreviousPage);
        }

        [Test]
        public void SetLoadingState_WhenPoolAlreadyEmpty_DoesNotLogAnyDestroyError()
        {
            // No LogAssert.Expect calls here on purpose: an unexpected error log would
            // fail the test, proving _ClearPool is a true no-op on an empty pool.
            TsvrcList list = CreateList();

            Assert.DoesNotThrow(() => list.SetLoadingState());

            Assert.AreEqual(STATE_LOADING, list.ListState);
        }

        [Test]
        public void SetData_Null_SetsEmptyStateAndNoPool()
        {
            TsvrcList list = CreateList();

            list.SetData(null);

            Assert.AreEqual(STATE_EMPTY, list.ListState);
            Assert.AreEqual(0, GetPool(list).Length);
        }

        [Test]
        public void SetData_Null_ResetsSelection()
        {
            TsvrcList list = CreateList();
            list.SetData(MakeData(3));
            list.OnItemSelected(1);
            Assert.AreEqual(1, list.SelectedIndex);

            ExpectDestroyErrors(3);
            list.SetData(null);

            Assert.AreEqual(-1, list.SelectedIndex);
        }

        [Test]
        public void SetData_EmptyList_SetsEmptyState()
        {
            TsvrcList list = CreateList();

            list.SetData(MakeData(0));

            Assert.AreEqual(STATE_EMPTY, list.ListState);
            Assert.AreEqual(0, GetPool(list).Length);
        }

        [Test]
        public void SetData_PopulatedList_SetsPopulatedStateAndBuildsPoolInOrder()
        {
            TsvrcList list = CreateList();

            list.SetData(MakeData(3));

            Assert.AreEqual(STATE_POPULATED, list.ListState);
            ListItem[] pool = GetPool(list);
            Assert.AreEqual(3, pool.Length);
            for (int i = 0; i < 3; i++)
            {
                var item = (ListItemTestSubclass)pool[i];
                Assert.AreEqual(1, item.OnBindCallCount);
                Assert.AreEqual(i, item.DataIndexAtLastBind);
                Assert.AreEqual((double)i, item.DataAtLastBind["index"].Double);
                Assert.IsTrue(item.gameObject.activeSelf);
            }
        }

        [Test]
        public void SetData_ResetsToFirstPage()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));
            ExpectDestroyErrors(2);
            list.SetPage(1);
            Assert.AreEqual(1, list.CurrentPage);

            ExpectDestroyErrors(2);
            list.SetData(MakeData(4));

            Assert.AreEqual(0, list.CurrentPage);
        }

        [Test]
        public void SetData_ReplacingPopulatedList_DestroysAndUnbindsOldItems()
        {
            TsvrcList list = CreateList();
            list.SetData(MakeData(2));
            var oldItems = new List<ListItemTestSubclass>();
            foreach (var item in GetPool(list)) oldItems.Add((ListItemTestSubclass)item);

            ExpectDestroyErrors(2);
            list.SetData(MakeData(3));

            foreach (var old in oldItems)
                Assert.AreEqual(1, old.OnUnbindCallCount);
            Assert.AreEqual(3, GetPool(list).Length);
        }

        [Test]
        public void SetData_NonNullData_DoesNotResetStaleSelectionIndex()
        {
            // Documents current behavior: only a null SetData call resets _selectedIndex.
            // Replacing with a new non-null dataset leaves a previously selected index in
            // place even though it may no longer correspond to anything meaningful in the
            // new dataset.
            TsvrcList list = CreateList();
            list.SetData(MakeData(5));
            list.OnItemSelected(4);

            ExpectDestroyErrors(5);
            list.SetData(MakeData(2));

            Assert.AreEqual(4, list.SelectedIndex, "Documents that stale selection survives a non-null SetData call.");
        }

        [Test]
        public void SetData_ListContainsNonDictionaryElement_BindsWithEmptyDictionaryFallback()
        {
            // _RebuildPool falls back to an empty DataDictionary when a DataList element
            // isn't itself a DataDictionary, rather than binding a raw/invalid token.
            var data = new DataList();
            data.Add(new DataToken("not a dictionary"));
            TsvrcList list = CreateList();

            list.SetData(data);

            var item = (ListItemTestSubclass)GetPool(list)[0];
            Assert.AreEqual(1, item.OnBindCallCount);
            Assert.IsNotNull(item.DataAtLastBind);
            Assert.AreEqual(0, item.DataAtLastBind.Count);
        }

        [Test]
        public void SetData_Empty_ShowsEmptyIndicatorHidesLoading()
        {
            GameObject loading = CreateIndicator("Loading");
            GameObject empty = CreateIndicator("Empty");
            TsvrcList list = CreateList(loadingIndicator: loading, emptyIndicator: empty);

            list.SetData(MakeData(0));

            Assert.IsFalse(loading.activeSelf);
            Assert.IsTrue(empty.activeSelf);
        }

        [Test]
        public void SetData_Populated_HidesBothIndicators()
        {
            GameObject loading = CreateIndicator("Loading");
            GameObject empty = CreateIndicator("Empty");
            TsvrcList list = CreateList(loadingIndicator: loading, emptyIndicator: empty);

            list.SetData(MakeData(2));

            Assert.IsFalse(loading.activeSelf);
            Assert.IsFalse(empty.activeSelf);
        }

        [Test]
        public void SetLoadingState_ShowsLoadingIndicatorAndClearsPool()
        {
            GameObject loading = CreateIndicator("Loading");
            GameObject empty = CreateIndicator("Empty");
            TsvrcList list = CreateList(loadingIndicator: loading, emptyIndicator: empty);
            list.SetData(MakeData(2));

            ExpectDestroyErrors(2);
            list.SetLoadingState();

            Assert.AreEqual(STATE_LOADING, list.ListState);
            Assert.IsTrue(loading.activeSelf);
            Assert.IsFalse(empty.activeSelf);
            Assert.AreEqual(0, GetPool(list).Length);
        }

        [Test]
        public void SetData_OnlyLoadingIndicatorAssigned_NoExceptionAndTogglesIndependently()
        {
            // _UpdateVisualState null-checks each indicator independently — verify that
            // holds when only one of the two is actually wired up (a supported "optional"
            // configuration per the class's own [Header("Visual States (optional)")]).
            GameObject loading = CreateIndicator("Loading");
            TsvrcList list = CreateList(loadingIndicator: loading, emptyIndicator: null);

            Assert.DoesNotThrow(() => list.SetData(MakeData(0)));

            Assert.IsFalse(loading.activeSelf);
        }

        [Test]
        public void SetData_OnlyEmptyIndicatorAssigned_NoExceptionAndTogglesIndependently()
        {
            GameObject empty = CreateIndicator("Empty");
            TsvrcList list = CreateList(loadingIndicator: null, emptyIndicator: empty);

            Assert.DoesNotThrow(() => list.SetData(MakeData(0)));

            Assert.IsTrue(empty.activeSelf);
        }

        [Test]
        public void PageCount_NoPaging_IsAlwaysOne()
        {
            TsvrcList list = CreateList(pageSize: -1);

            list.SetData(MakeData(10));

            Assert.AreEqual(1, list.PageCount);
        }

        [Test]
        public void PageCount_WithPaging_RoundsUp()
        {
            TsvrcList list = CreateList(pageSize: 2);

            list.SetData(MakeData(5));

            Assert.AreEqual(3, list.PageCount);
        }

        [Test]
        public void PageCount_EmptyDataWithPaging_ReturnsZero()
        {
            // Documents current behavior: unlike the no-paging case (which always
            // reports PageCount == 1), an empty dataset under paging reports 0, not 1 —
            // asymmetric with the "always at least one page" framing used elsewhere.
            TsvrcList list = CreateList(pageSize: 2);

            list.SetData(MakeData(0));

            Assert.AreEqual(0, list.PageCount);
        }

        [Test]
        public void SetPage_BuildsCorrectSliceOfItems()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));

            ExpectDestroyErrors(2);
            list.SetPage(1);

            ListItem[] pool = GetPool(list);
            Assert.AreEqual(2, pool.Length);
            Assert.AreEqual(2, ((ListItemTestSubclass)pool[0]).DataIndexAtLastBind);
            Assert.AreEqual(3, ((ListItemTestSubclass)pool[1]).DataIndexAtLastBind);
        }

        [Test]
        public void SetPage_DataCountExactMultipleOfPageSize_LastPageIsFullNotPartial()
        {
            // A different CeilToInt boundary than the other pagination tests: 4 items at
            // pageSize 2 divides evenly, so PageCount must be exactly 2 (not 3), and the
            // last page must be full-sized (2 items), not a partial remainder.
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(4));

            Assert.AreEqual(2, list.PageCount);

            ExpectDestroyErrors(2);
            list.SetPage(1);

            ListItem[] pool = GetPool(list);
            Assert.AreEqual(2, pool.Length);
            Assert.AreEqual(2, ((ListItemTestSubclass)pool[0]).DataIndexAtLastBind);
            Assert.AreEqual(3, ((ListItemTestSubclass)pool[1]).DataIndexAtLastBind);
            Assert.IsFalse(list.HasNextPage);
        }

        [Test]
        public void SetPage_LastPage_BuildsPartialSlice()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));

            ExpectDestroyErrors(2);
            list.SetPage(2);

            ListItem[] pool = GetPool(list);
            Assert.AreEqual(1, pool.Length);
            Assert.AreEqual(4, ((ListItemTestSubclass)pool[0]).DataIndexAtLastBind);
        }

        [Test]
        public void SetPage_ClampsAboveRange()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));

            ExpectDestroyErrors(2);
            list.SetPage(100);

            Assert.AreEqual(2, list.CurrentPage);
        }

        [Test]
        public void SetPage_ClampsBelowRange()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));

            ExpectDestroyErrors(2);
            list.SetPage(1);
            ExpectDestroyErrors(2);
            list.SetPage(-5);

            Assert.AreEqual(0, list.CurrentPage);
        }

        [Test]
        public void SetPage_WhileNotPopulated_IsNoOp()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(null);

            list.SetPage(1);

            Assert.AreEqual(0, list.CurrentPage);
        }

        [Test]
        public void NextPage_AdvancesPage()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));

            ExpectDestroyErrors(2);
            list.NextPage();

            Assert.AreEqual(1, list.CurrentPage);
        }

        [Test]
        public void NextPage_AtLastPage_IsNoOp()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));
            ExpectDestroyErrors(2);
            list.SetPage(2);
            Assert.IsFalse(list.HasNextPage);

            list.NextPage();

            Assert.AreEqual(2, list.CurrentPage);
        }

        [Test]
        public void PreviousPage_AtFirstPage_IsNoOp()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));
            Assert.IsFalse(list.HasPreviousPage);

            list.PreviousPage();

            Assert.AreEqual(0, list.CurrentPage);
        }

        [Test]
        public void HasNextPage_HasPreviousPage_ReflectBoundaries()
        {
            TsvrcList list = CreateList(pageSize: 2);
            list.SetData(MakeData(5));

            Assert.IsTrue(list.HasNextPage);
            Assert.IsFalse(list.HasPreviousPage);

            ExpectDestroyErrors(2);
            list.NextPage();
            Assert.IsTrue(list.HasNextPage);
            Assert.IsTrue(list.HasPreviousPage);

            ExpectDestroyErrors(2);
            list.NextPage();
            Assert.IsFalse(list.HasNextPage);
            Assert.IsTrue(list.HasPreviousPage);
        }

        [Test]
        public void OnItemSelected_SetsSelectedIndexAndEmitsEvent()
        {
            TsvrcList list = CreateList();
            var listenerGO = new GameObject("Listener");
            _spawned.Add(listenerGO);
            var listener = listenerGO.AddComponent<TsListenerDouble>();
            list.TsSubscribe(listener, TsvrcList.OnItemSelectedEvent, nameof(TsListenerDouble.CallbackA));

            list.OnItemSelected(2);

            Assert.AreEqual(2, list.SelectedIndex);
            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void OnItemSelected_IndexOutOfDataRange_NotValidatedAndStillSetsAndEmits()
        {
            // Documents current behavior: OnItemSelected performs no bounds checking
            // against the current dataset — any int is accepted and emitted as-is.
            TsvrcList list = CreateList();
            list.SetData(MakeData(2));
            var listenerGO = new GameObject("Listener");
            _spawned.Add(listenerGO);
            var listener = listenerGO.AddComponent<TsListenerDouble>();
            list.TsSubscribe(listener, TsvrcList.OnItemSelectedEvent, nameof(TsListenerDouble.CallbackA));

            list.OnItemSelected(9999);

            Assert.AreEqual(9999, list.SelectedIndex);
            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void ClearSelection_ResetsSelectedIndexToMinusOne()
        {
            TsvrcList list = CreateList();
            list.SetData(MakeData(3));
            list.OnItemSelected(1);

            list.ClearSelection();

            Assert.AreEqual(-1, list.SelectedIndex);
        }

    }
}
