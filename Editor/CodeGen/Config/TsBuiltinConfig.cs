#if UNITY_EDITOR
using Tsvrc.Config;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Library-wide Globals/Pool/Factory registrations shipped with tsvrc itself, merged with
    /// each world's own <see cref="TsConfig"/> at generate time.
    /// </summary>
    public class TsBuiltinConfig : ScriptableObject
    {
        /// <summary>Library-internal objects exposed as named fields directly on the generated TsGenerated root, regardless of world config.</summary>
        [Tooltip("Library-internal objects exposed as named fields directly on the generated TsGenerated root, regardless of world config.")]
        public TsGroupedEntry[] GlobalEntries;

        /// <summary>Nested groups organizing <see cref="GlobalEntries"/>.</summary>
        [Tooltip("Nested groups organizing GlobalEntries for browsing. Purely organizational - group names do not affect generated field names.")]
        public TsGroup[] GlobalGroups;

        /// <summary>The next unused id to assign in <see cref="GlobalGroups"/>.</summary>
        public int GlobalNextGroupId = 1;

        /// <summary>UdonSharpBehaviour prefabs always included in the generated pool as library builtins, regardless of world config.</summary>
        [Tooltip("UdonSharpBehaviour prefabs always included in the generated pool as library builtins, regardless of world config.")]
        public TsGroupedEntry[] PoolEntries;

        /// <summary>Nested groups organizing <see cref="PoolEntries"/>.</summary>
        [Tooltip("Nested groups organizing PoolEntries for browsing. Purely organizational.")]
        public TsGroup[] PoolGroups;

        /// <summary>The next unused id to assign in <see cref="PoolGroups"/>.</summary>
        public int PoolNextGroupId = 1;

        /// <summary>Library-internal factory prefabs, regardless of world config.</summary>
        [Tooltip("Library-internal factory prefabs. Each entry's full group ancestor chain becomes a prefix on the generated Create{...}(Transform parent) method name.")]
        public TsGroupedEntry[] FactoryEntries;

        /// <summary>Nested groups organizing <see cref="FactoryEntries"/>.</summary>
        [Tooltip("Nested groups organizing FactoryEntries. Each group's (and its ancestors') name becomes a prefix on the generated Create method name for prefabs inside it.")]
        public TsGroup[] FactoryGroups;

        /// <summary>The next unused id to assign in <see cref="FactoryGroups"/>.</summary>
        public int FactoryNextGroupId = 1;
    }
}
#endif
