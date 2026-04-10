using TMPro;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.UI
{
    /// <summary>
    /// Default single-label item for <see cref="TsList"/>.
    /// Reads the <c>"label"</c> key from the bound <see cref="DataDictionary"/>.
    /// Extend <see cref="TsListItem"/> directly when richer content is needed.
    /// </summary>
    [UdonSharp.UdonBehaviourSyncMode(UdonSharp.BehaviourSyncMode.None)]
    public class TsListLabel : TsListItem
    {
        [SerializeField] private TextMeshProUGUI _label;

        protected override void _OnBind()
        {
            if (_label == null) return;
            DataToken token;
            _label.text = _itemData != null && _itemData.TryGetValue("label", TokenType.String, out token)
                ? token.String
                : string.Empty;
        }

        protected override void _OnUnbind()
        {
            if (_label != null) _label.text = string.Empty;
        }
    }
}
