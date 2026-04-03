using UnityEngine;

namespace Tsvrc.Core
{
    // Library-internal config stored as a ScriptableObject asset at a fixed path.
    // Not intended to be modified by end users. Edit via the Unity Inspector on the asset directly.
    // Loaded by the compiler at Assets/Tsvrc/InternalConfig.asset.
    public class InternalTsvrcConfig : ScriptableObject
    {
        [Tooltip("Library-internal objects exposed as named fields on CompiledTsvrc. These are framework-level references used by the core behaviour layer.")]
        public Object[] Singletons;
        [Tooltip("Library-internal TsvrcBehaviour prefabs registered as pool slots. The compiler places instances in the scene before play so VRChat assigns them stable network IDs, enabling network events.")]
        public TsvrcBehaviour[] PoolPrefabs;
    }
}

