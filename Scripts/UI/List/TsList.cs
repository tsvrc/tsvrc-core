using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;

namespace Tsvrc.UI
{
    /// <summary>
    /// Scrollable list driven by a <see cref="DataList"/> of <see cref="DataDictionary"/> entries.
    /// Items are Instantiated from a prefab when they enter the viewport and Destroyed when they leave.
    /// Scroll direction (vertical or horizontal) is detected automatically from the <see cref="ScrollRect"/>.
    /// When both axes are enabled the list virtualizes on the vertical axis.
    /// Item size is read from the prefab's <see cref="RectTransform"/> on the scroll axis at startup.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TsList : TsvrcBehaviour
    {
        protected const int STATE_LOADING = 0;
        protected const int STATE_EMPTY = 1;
        protected const int STATE_POPULATED = 2;

        [Header("References")]
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _content;
        [SerializeField] private GameObject _itemPrefab;

        [Header("Visual States (optional)")]
        [SerializeField] private GameObject _loadingIndicator;
        [SerializeField] private GameObject _emptyIndicator;

        private DataList _data = null;
        // pool[i] corresponds to data index (_firstVisible + i)
        private TsListItem[] _pool = new TsListItem[0];
        private int _listState = STATE_LOADING;
        private int _selectedIndex = -1;
        private float _itemSize = 0f;
        private bool _isHorizontal = false;
        private int _firstVisible = 0;
        private int _lastVisible = -1;
        private float _lastScrollNorm = -1f;
        private float _lastViewportSize = -1f;

        public int ListState => _listState;
        public int SelectedIndex => _selectedIndex;

        protected override void TsStart()
        {
            if (_scrollRect != null)
                _isHorizontal = _scrollRect.horizontal && !_scrollRect.vertical;

            if (_itemPrefab != null)
            {
                var rt = (RectTransform)_itemPrefab.transform;
                _itemSize = _isHorizontal ? rt.rect.width : rt.rect.height;
            }
            SetLoadingState();
        }

        #region Public API

        /// <summary>Loads data and populates the list from the top.</summary>
        public void SetData(DataList data)
        {
            _data = data;
            if (_scrollRect != null)
            {
                if (_isHorizontal) _scrollRect.horizontalNormalizedPosition = 0f;
                else _scrollRect.verticalNormalizedPosition = 1f;
            }
            _SetState(data != null && data.Count > 0 ? STATE_POPULATED : STATE_EMPTY);
        }

        /// <summary>Shows the loading indicator and destroys all live items.</summary>
        public void SetLoadingState()
        {
            _SetState(STATE_LOADING);
        }

        /// <summary>Called by <see cref="TsListItem._OnItemPressed"/>.</summary>
        public void OnItemSelected(int dataIndex)
        {
            _selectedIndex = dataIndex;
            TsEmit("OnItemSelected");
        }

        #endregion

        #region Private

        private void Update()
        {
            if (_listState != STATE_POPULATED || _scrollRect == null) return;

            float scrollNorm = _isHorizontal
                ? _scrollRect.horizontalNormalizedPosition
                : _scrollRect.verticalNormalizedPosition;
            float viewportSize = _GetViewportSize();
            if (scrollNorm == _lastScrollNorm && viewportSize == _lastViewportSize) return;
            _lastScrollNorm = scrollNorm;
            _lastViewportSize = viewportSize;

            int newFirst = _ComputeFirstVisible(viewportSize);
            int newLast = _ComputeLastVisible(newFirst, viewportSize);
            if (newFirst != _firstVisible || newLast != _lastVisible)
                _SyncPool(newFirst, newLast);
        }

        private void _SetState(int state)
        {
            _listState = state;
            _ClearPool();
            _UpdateVisualState();
            if (state == STATE_POPULATED)
            {
                float viewportSize = _scrollRect != null ? _GetViewportSize() : 0f;
                int first = _ComputeFirstVisible(viewportSize);
                _SyncPool(first, _ComputeLastVisible(first, viewportSize));
            }
        }

        private void _UpdateVisualState()
        {
            if (_loadingIndicator != null) _loadingIndicator.SetActive(_listState == STATE_LOADING);
            if (_emptyIndicator != null) _emptyIndicator.SetActive(_listState == STATE_EMPTY);

            if (_content != null)
            {
                var size = _content.sizeDelta;
                float total = (_listState == STATE_POPULATED && _data != null && _itemSize > 0f)
                    ? _data.Count * _itemSize
                    : 0f;
                if (_isHorizontal) size.x = total;
                else size.y = total;
                _content.sizeDelta = size;
            }
        }

        // Returns the viewport size on the scroll axis. Falls back to the ScrollRect's own
        // transform if the viewport field is not assigned in the inspector.
        private float _GetViewportSize()
        {
            RectTransform vp = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : (RectTransform)_scrollRect.transform;
            return _isHorizontal ? vp.rect.width : vp.rect.height;
        }

        private int _ComputeFirstVisible(float viewportSize)
        {
            if (_scrollRect == null || _itemSize <= 0f || _data == null || _data.Count == 0) return 0;
            float contentSize = _data.Count * _itemSize;
            float norm = _isHorizontal ? _scrollRect.horizontalNormalizedPosition : (1f - _scrollRect.verticalNormalizedPosition);
            float offset = norm * Mathf.Max(0f, contentSize - viewportSize);
            return Mathf.Clamp(Mathf.FloorToInt(offset / _itemSize), 0, _data.Count - 1);
        }

        private int _ComputeLastVisible(int firstVisible, float viewportSize)
        {
            if (_itemSize <= 0f || _data == null || _data.Count == 0) return -1;
            return Mathf.Min(firstVisible + Mathf.CeilToInt(viewportSize / _itemSize), _data.Count - 1);
        }

        private void _SyncPool(int newFirst, int newLast)
        {
            int oldLen = _pool.Length;

            // Unbind and destroy items that left the visible range
            for (int i = 0; i < oldLen; i++)
            {
                if (_pool[i] == null) continue;
                int di = _firstVisible + i;
                if (di < newFirst || di > newLast)
                {
                    _pool[i].Unbind();
                    Destroy(_pool[i].gameObject);
                    _pool[i] = null;
                }
            }

            int needed = newFirst <= newLast ? newLast - newFirst + 1 : 0;
            if (needed == 0)
            {
                _pool = new TsListItem[0];
                _firstVisible = newFirst;
                _lastVisible = newLast;
                return;
            }

            TsListItem[] newPool = new TsListItem[needed];

            for (int di = newFirst; di <= newLast; di++)
            {
                int newSlot = di - newFirst;
                int oldSlot = di - _firstVisible;

                // O(1) reuse: pool[i] always maps to data index (_firstVisible + i)
                if (oldSlot >= 0 && oldSlot < oldLen && _pool[oldSlot] != null)
                {
                    newPool[newSlot] = _pool[oldSlot];
                    continue;
                }

                GameObject go = Instantiate(_itemPrefab, _content);
                TsListItem item = go.GetComponent<TsListItem>();
                if (item == null) { Destroy(go); continue; }

                RectTransform rt = (RectTransform)go.transform;
                if (_isHorizontal)
                {
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 0.5f);
                    rt.anchoredPosition = new Vector2(di * _itemSize, 0f);
                }
                else
                {
                    rt.anchorMin = new Vector2(0f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -di * _itemSize);
                }

                DataToken token = _data[di];
                DataDictionary data = token.TokenType == TokenType.DataDictionary
                    ? token.DataDictionary
                    : new DataDictionary();

                item.TsConstruct(this);
                item.Bind(this, di, data);
                newPool[newSlot] = item;
            }

            _pool = newPool;
            _firstVisible = newFirst;
            _lastVisible = newLast;
        }

        private void _ClearPool()
        {
            int len = _pool.Length;
            for (int i = 0; i < len; i++)
            {
                if (_pool[i] == null) continue;
                _pool[i].Unbind();
                Destroy(_pool[i].gameObject);
            }
            _pool = new TsListItem[0];
            _firstVisible = 0;
            _lastVisible = -1;
        }

        #endregion
    }
}
