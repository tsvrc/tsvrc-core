using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    // EditorOnly — scanned by TsvrcCompiler, stripped from VRChat build.
    public class TsvrcConfig : UdonSharpBehaviour
    {
        [Tooltip("The single TsvrcInstance for this world. If set, TsConstruct(_ts) and OnInstanceStart() are called on it after all other constructs.")]
        public TsvrcInstance Instance;
        [Tooltip("Required. Internal core config — do NOT modify or reassign. Provides framework-level entries compiled into the Core region of CompiledTsvrc.")]
        [HideInInspector][SerializeField] public InternalTsvrcConfig InternalTsvrcConfig;

        [Tooltip("Scene objects exposed as named singleton fields on CompiledTsvrc. Drop a GameObject or Component here — each entry becomes a typed _ts.FieldName accessor available to all TsvrcBehaviours.")]
        public Object[] Singletons;
        [Tooltip("TsvrcBehaviours placed in the scene at compile time as pool slots. Because they exist in the scene before play, VRChat assigns them network IDs so they can receive UdonSharp network events — unlike runtime-instantiated objects. Each entry generates a Get<TypeName>() method on CompiledTsvrc that activates and returns an available slot.")]
        public TsvrcBehaviour[] TsvrcBehaviourPool;
        [Tooltip("TsvrcBehaviours that are constructed directly in the scene (always active, not pooled). TsConstruct(_ts) is called on each during startup.")]
        public TsvrcBehaviour[] TsvrcBehaviourConstruct;

        [Tooltip("Disk prefab assets to register as factories. For each entry a Create{Name}(Transform parent) method is generated on CompiledTsvrc that instantiates a new copy at runtime. " +
                 "WARNING: runtime-instantiated objects are NOT assigned a VRChat network ID, they cannot send or receive VRC network events " +
                 "(e.g. OnDeserialization, SendCustomNetworkEvent, OnPlayerJoined). For networked objects use the Pool instead.")]
        public GameObject[] FactoryPrefabs;
    }
}