using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    // EditorOnly — scanned by TsvrcCompiler as the *core* config, stripped from VRChat build.
    // Not intended to be modified by end users. Place this as a child of TsvrcConfig.
    public class InternalTsvrcConfig : UdonSharpBehaviour
    {
        [Tooltip("Core scene objects exposed as named singleton fields on CompiledTsvrc (core section). Drop a GameObject or Component here — each entry becomes a typed _ts.FieldName accessor available to all TsvrcBehaviours.")]
        public Object[] Singletons;
        [Tooltip("Core TsvrcBehaviours placed in the scene at compile time as pool slots (core section). Scene-placed so VRChat assigns network IDs — enabling UdonSharp network events. Each entry generates a Get<TypeName>() method on CompiledTsvrc.")]
        public TsvrcBehaviour[] TsvrcBehaviourPool;
    }
}
