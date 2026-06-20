#if UNITY_EDITOR
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Editor
{
    public class TsvrcBuiltinConfig : ScriptableObject
    {
        [Tooltip("Library-internal objects exposed as named fields on TsvrcSingletonBehaviour, regardless of world config.")]
        public Object[] Singletons;
        [Tooltip("TsvrcProcess prefabs always included in the generated pool as library builtins, regardless of world config.")]
        public TsvrcProcess[] PoolPrefabs;
        [Tooltip("Library-internal factory groups. Each group generates Create{GroupName}{PrefabName}(Transform parent) factory methods on TsvrcGenerated.")]
        public TsvrcFactoryGroup[] Factories;
    }
}
#endif
