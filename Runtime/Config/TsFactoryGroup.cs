using System;
using UnityEngine;

namespace Tsvrc.Config
{
    /// <summary>
    /// Serializable data for a named factory group.
    /// Used by both <see cref="TsConfig"/> (scene) and <c>TsBuiltinConfig</c>
    /// (ScriptableObject asset, <c>Editor/CodeGen/Config/TsBuiltinConfig.cs</c>) so the
    /// compiler reads factory entries through the same type regardless of source.
    /// </summary>
    [Serializable]
    public class TsFactoryGroup
    {
        [Tooltip("Prefix added to every Create method generated from this group. Example: \"Maze\" generates CreateMazeBullet(Transform parent). Leave empty to omit the prefix.")]
        public string GroupName;

        [Tooltip("Prefabs to register. Each entry generates a Create{GroupName}{PrefabName}(Transform parent) method on TsGenerated. You may drag a GameObject or any Component, the compiler always resolves to the root GameObject.")]
        public UnityEngine.Object[] Prefabs;
    }
}
