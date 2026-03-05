using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    public class TsvrcInstance : UdonSharpBehaviour
    {
        [SerializeField] protected TsvrcSingleton _singleton;

        public Object[] Singletons;
        public TsvrcBehaviour[] Behaviours;

        protected virtual void Start()
        {
            _singleton.TsConstruct(instance: this);
        }
    }
}