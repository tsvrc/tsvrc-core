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
        public InternalTsvrcConfig InternalTsvrcConfig;

        [Tooltip("Scene objects exposed as named singleton fields on CompiledTsvrc. Drop a GameObject or Component here — each entry becomes a typed _ts.FieldName accessor available to all TsvrcBehaviours.")]
        public Object[] Singletons;
        [Tooltip("TsvrcBehaviours placed in the scene at compile time as pool slots. Because they exist in the scene before play, VRChat assigns them network IDs so they can receive UdonSharp network events — unlike runtime-instantiated objects. Each entry generates a Get<TypeName>() method on CompiledTsvrc that activates and returns an available slot.")]
        public TsvrcBehaviour[] TsvrcBehaviourPool;
        [Tooltip("TsvrcBehaviours that are constructed directly in the scene (always active, not pooled). TsConstruct(_ts) is called on each during startup.")]
        public TsvrcBehaviour[] TsvrcBehaviourConstruct;
    }
}