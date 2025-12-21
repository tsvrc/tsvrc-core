using UdonSharp;
using UnityEngine;

namespace Tsvrc.UI
{
    public class TsvrcList : UdonSharpBehaviour
    {
        [SerializeField] protected Transform _container;
        [SerializeField] protected TsvrcListItem[] _items;


        public void AddItem(TsvrcListItem item)
        {
            item.gameObject.SetActive(true);
            item.transform.SetParent(_container, false);

            _items = _container.GetComponentsInChildren<TsvrcListItem>(true);
        }

        public void RemoveItem(TsvrcListItem item)
        {
            if (item != null)
            {
                Destroy(item.gameObject);
                _items = _container.GetComponentsInChildren<TsvrcListItem>(true);
            }
        }
    }
}
