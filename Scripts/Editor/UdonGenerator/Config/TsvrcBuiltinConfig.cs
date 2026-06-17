#if UNITY_EDITOR
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    public class TsvrcBuiltinConfig : ScriptableObject
    {
        [Tooltip("Library-internal objects exposed as named fields on TsvrcSingletonBehaviour, regardless of world config.")]
        public Object[] Singletons;
        [Tooltip("TsvrcProcess prefabs always included in the generated pool as library builtins, regardless of world config.")]
        public TsvrcProcess[] PoolPrefabs;
    }
}
#endif
