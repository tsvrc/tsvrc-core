using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.UI
{
    /// <summary>
    /// Base item for <see cref="TsList"/>.
    /// Receives a <see cref="DataDictionary"/> on bind; override <see cref="_OnBind"/> to populate UI.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ListItem : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        protected TsList _list = null;
        protected DataDictionary _itemData = null;
        protected int _dataIndex = -1;
        private bool _isBound = false;

        public int DataIndex => _dataIndex;
        public bool IsBound => _isBound;

        /// <summary>Called by <see cref="TsList"/> when this item enters the viewport.</summary>
        public void Bind(TsList list, int dataIndex, DataDictionary data)
        {
            _list = list;
            _dataIndex = dataIndex;
            _itemData = data;
            _isBound = true;
            _OnBind();
        }

        /// <summary>Called by <see cref="TsList"/> when this item is destroyed from the viewport.</summary>
        public void Unbind()
        {
            _isBound = false;
            _OnUnbind();
            _dataIndex = -1;
            _itemData = null;
            _list = null;
        }

        /// <summary>Override to populate UI children from <see cref="_itemData"/>.</summary>
        protected virtual void _OnBind() { }

        /// <summary>Override to clear UI children. <see cref="_itemData"/> and <see cref="_dataIndex"/> are still valid here.</summary>
        protected virtual void _OnUnbind() { }

        /// <summary>Wire to a Button.onClick. Emits "OnItemSelected" on the owning list.</summary>
        public void _OnItemPressed()
        {
            if (!_isBound || _list == null) return;
            _list.OnItemSelected(_dataIndex);
        }
    }
}
