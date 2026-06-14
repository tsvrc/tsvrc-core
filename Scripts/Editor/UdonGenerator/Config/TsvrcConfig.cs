#if UNITY_EDITOR
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    public class TsvrcConfig : ScriptableObject
    {
        public TsvrcInstance Instance;
        public Object[] Singletons;
        public Object[] PooledObjects;
        public TsvrcBehaviour[] Constructs;
        public TsvrcFactoryGroup[] Factories;
    }
}
#endif
