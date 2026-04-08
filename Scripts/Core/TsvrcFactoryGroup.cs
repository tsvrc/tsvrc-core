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
        [Tooltip("Prefix added to every Create method generated from this group. Example: \"Maze\" generates CreateMazeBullet(Transform parent). Leave empty to omit the prefix.")]
        public string GroupName;

        [Tooltip("Prefabs to register in this factory group. Each entry generates a Create{GroupName}{PrefabName}(Transform parent) method on CompiledTsvrc. You may drag a GameObject or any Component (e.g. a UdonSharpBehaviour) — the compiler always resolves to the root GameObject.")]
        public Object[] Prefabs;
    }
}
