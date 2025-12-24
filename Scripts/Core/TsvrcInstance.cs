using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    public class TsvrcInstance : UdonSharpBehaviour
    {
        [SerializeField] private TsvrcSingleton _singleton;

        public void Start()
        {
            _singleton.TsConstruct(instance: this);
        }
    }
}