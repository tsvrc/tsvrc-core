using System;
using UnityEngine;

namespace Tsvrc.Core
{
    /// <summary>
    /// Serializable data for a named factory group.
    /// Used by both <see cref="TsvrcConfig"/> (scene) and <see cref="InternalTsvrcConfig"/>
    /// (ScriptableObject asset) so the compiler reads factory entries through the same type
    /// regardless of source.
    /// </summary>
    [Serializable]
    public class TsvrcFactoryGroup
    {
        [Tooltip("Prefix added to every Create method generated from this group. Example: \"Maze\" generates CreateMazeBullet(Transform parent). Leave empty to omit the prefix.")]
        public string GroupName;

        [Tooltip("Prefabs to register. Each entry generates a Create{GroupName}{PrefabName}(Transform parent) method on CompiledTsvrc. You may drag a GameObject or any Component — the compiler always resolves to the root GameObject.")]
        public UnityEngine.Object[] Prefabs;
    }
}
