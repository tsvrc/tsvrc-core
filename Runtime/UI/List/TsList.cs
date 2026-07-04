using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.UI
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TsList : TsvrcBehaviour
    {
        public const string OnItemSelectedEvent = "OnItemSelected";
        protected const int STATE_LOADING = 0;
        protected const int STATE_EMPTY = 1;
        protected const int STATE_POPULATED = 2;

        [Header("References")]
        [Tooltip("Parent transform where item GameObjects are instantiated.\n" +
                 "Typically the Content of a ScrollRect.")]
        [SerializeField] private Transform _itemContainer;
        [Tooltip("Prefab with a TsListItem component that represents a single list entry.")]
        [SerializeField] private TsListItem _itemPrefab;

        [Header("Pagination")]
        [Tooltip("Number of items per page. Set to -1 to load all items at once with no pagination.")]
        [SerializeField] private int _pageSize = -1;

        [Header("Visual States (optional)")]
        [Tooltip("Shown while the list is in the loading state.")]
        [SerializeField] private GameObject _loadingIndicator;
        [Tooltip("Shown when SetData is called with null or an empty list.")]
        [SerializeField] private GameObject _emptyIndicator;

        private DataList _data = null;
        private TsListItem[] _pool = new TsListItem[0];
        private int _listState = STATE_LOADING;
        private int _selectedIndex = -1;
        private int _currentPage = 0;

        public int ListState => _listState;
        public int SelectedIndex => _selectedIndex;
        public int CurrentPage => _currentPage;
        public int PageCount => (_data != null && _pageSize > 0) ? Mathf.CeilToInt((float)_data.Count / _pageSize) : 1;
        public bool HasNextPage => _currentPage < PageCount - 1;
        public bool HasPreviousPage => _currentPage > 0;

        #region Public API

        /// <summary>Loads data and resets to the first page.</summary>
        public void SetData(DataList data)
        {
            _data = data;
            _currentPage = 0;
            if (data == null) _selectedIndex = -1;
            _SetState(data != null && data.Count > 0 ? STATE_POPULATED : STATE_EMPTY);
        }

        /// <summary>Shows the loading indicator and destroys all live items.</summary>
        public void SetLoadingState()
        {
            _SetState(STATE_LOADING);
        }

        /// <summary>Destroys current items and instantiates items for the given page.</summary>
        public void SetPage(int page)
        {
            if (_listState != STATE_POPULATED || _data == null) return;
            _currentPage = Mathf.Clamp(page, 0, PageCount - 1);
            _RebuildPool();
        }

        public void NextPage()
        {
            if (HasNextPage) SetPage(_currentPage + 1);
        }

        public void PreviousPage()
        {
            if (HasPreviousPage) SetPage(_currentPage - 1);
        }

        /// <summary>Called by TsListItem when pressed.</summary>
        public void OnItemSelected(int dataIndex)
        {
            _selectedIndex = dataIndex;
            TsEmit(OnItemSelectedEvent);
        }

        public void ClearSelection()
        {
            _selectedIndex = -1;
        }

        #endregion

        #region Private

        private void _SetState(int state)
        {
            _listState = state;
            _ClearPool();
            _UpdateVisualState();
            if (state == STATE_POPULATED)
                _RebuildPool();
        }

        private void _UpdateVisualState()
        {
            if (_loadingIndicator != null) _loadingIndicator.SetActive(_listState == STATE_LOADING);
            if (_emptyIndicator != null) _emptyIndicator.SetActive(_listState == STATE_EMPTY);
        }

        private void _RebuildPool()
        {
            _ClearPool();
            if (_data == null || _itemPrefab == null || _itemContainer == null) return;

            int start = _currentPage * (_pageSize > 0 ? _pageSize : int.MaxValue);
            int end = _pageSize > 0 ? Mathf.Min(start + _pageSize, _data.Count) : _data.Count;
            int count = end - start;
            if (count <= 0) return;

            _pool = new TsListItem[count];
            for (int i = 0; i < count; i++)
            {
                int di = start + i;
                GameObject go = Instantiate(_itemPrefab.gameObject, _itemContainer);
                TsListItem item = go.GetComponent<TsListItem>();
                if (item == null) { Destroy(go); continue; }

                DataToken token = _data[di];
                DataDictionary data = token.TokenType == TokenType.DataDictionary
                    ? token.DataDictionary
                    : new DataDictionary();

                item.TsConstruct(this);
                item.Bind(this, di, data);
                go.SetActive(true);
                _pool[i] = item;
            }
        }

        private void _ClearPool()
        {
            for (int i = 0; i < _pool.Length; i++)
            {
                if (_pool[i] == null) continue;
                _pool[i].Unbind();
                Destroy(_pool[i].gameObject);
            }
            _pool = new TsListItem[0];
        }

        #endregion
    }
}
