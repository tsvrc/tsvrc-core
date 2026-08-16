using System.Collections.Generic;
using Tsvrc.Testing.Framework;
using Tsvrc.UI;
using UnityEngine;

namespace Tsvrc.Testing.UI
{
    /// <summary>
    /// Generic <see cref="ListItem"/> double for any world testing TsvrcList-driven UI: records
    /// bind/unbind calls and the data last bound, without needing a real prefab hierarchy.
    /// </summary>
    public class TestListItem : ListItem
    {
        public int OnBindCallCount;
        public int OnUnbindCallCount;
        public VRC.SDK3.Data.DataDictionary DataAtLastBind;
        public int DataIndexAtLastBind;

        protected override void _OnBind()
        {
            OnBindCallCount++;
            DataAtLastBind = _itemData;
            DataIndexAtLastBind = _dataIndex;
        }

        protected override void _OnUnbind()
        {
            OnUnbindCallCount++;
        }

        public TsvrcList ListField => _list;
    }

    /// <summary>
    /// Wires a bare <see cref="TsvrcList"/> (item container + prefab + page size) the way any
    /// world's tests need to reach it from code - via <see cref="PrivateFieldAccess"/> against
    /// its private serialized fields - instead of every world re-deriving the same reflection
    /// wiring by hand.
    /// </summary>
    public static class TsvrcListTestBuilder
    {
        /// <summary>Builds a TsvrcList plus its container/prefab GameObjects, tracking every
        /// spawned GameObject in <paramref name="spawned"/> for the caller's own teardown.</summary>
        public static TsvrcList Build(List<GameObject> spawned, int pageSize = -1)
        {
            var listGo = new GameObject(nameof(TsvrcList));
            spawned.Add(listGo);
            var list = listGo.AddComponent<TsvrcList>();

            var containerGo = new GameObject("Container");
            containerGo.transform.SetParent(listGo.transform);
            spawned.Add(containerGo);

            var prefabGo = new GameObject("ItemPrefab");
            spawned.Add(prefabGo);
            var prefabItem = prefabGo.AddComponent<TestListItem>();

            PrivateFieldAccess.SetField(list, "_itemContainer", containerGo.transform);
            PrivateFieldAccess.SetField(list, "_itemPrefab", prefabItem);
            PrivateFieldAccess.SetField(list, "_pageSize", pageSize);

            return list;
        }
    }
}
