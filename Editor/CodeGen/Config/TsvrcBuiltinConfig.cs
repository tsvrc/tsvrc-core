#if UNITY_EDITOR
using Tsvrc.Config;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Editor
{
    public class TsvrcBuiltinConfig : ScriptableObject
    {
        [Tooltip("Library-internal objects exposed as named fields directly on the generated TsvrcGenerated root, regardless of world config.")]
        public Object[] Singletons;
        [Tooltip("UdonSharpBehaviour prefabs always included in the generated pool as library builtins, regardless of world config.")]
        public UdonSharpBehaviour[] PoolPrefabs;
        [Tooltip("Library-internal factory groups. Each group generates Create{GroupName}{PrefabName}(Transform parent) factory methods on TsvrcGenerated.")]
        public TsvrcFactoryGroup[] Factories;
    }
}
#endif
