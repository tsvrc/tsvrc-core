using UnityEngine;

namespace Tsvrc.Core
{
    /// <summary>
    /// Editor-only data container for a named factory group.
    /// Each group lives as a child GameObject under TsvrcConfig in the scene.
    /// The compiler scans all groups and generates Create{GroupName}{PrefabName}(Transform parent)
    /// methods on CompiledTsvrc for every prefab in each group.
    /// </summary>
    [AddComponentMenu("")]
    public class TsvrcFactoryGroup : MonoBehaviour
    {
        [Tooltip("Prefix added to every Create method generated from this group. E.g. \"Maze\" → CreateMazeBullet(). Leave empty to omit the prefix.")]
        public string GroupName;

        [Tooltip("Prefabs to register as factories in this group.")]
        public GameObject[] Prefabs;
    }
}
