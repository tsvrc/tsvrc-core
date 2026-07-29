using Tsvrc.UI;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class ListItemTestSubclass : ListItem
    {
        public int OnBindCallCount;
        public int OnUnbindCallCount;
        public DataDictionary DataAtLastBind;
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

        public TsList ListField => _list;
    }
}
