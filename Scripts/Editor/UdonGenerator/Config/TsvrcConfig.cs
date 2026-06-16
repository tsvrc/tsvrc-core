#if UNITY_EDITOR
using Tsvrc.Core;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    public class TsvrcConfig : ScriptableObject
    {
        public Object[] Singletons;
        public UdonSharpBehaviour[] PooledObjects;
        public TsvrcBehaviour[] Constructs;
        public TsvrcFactoryGroup[] Factories;
    }
}
#endif
