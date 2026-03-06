using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    // EditorOnly — scanned by TsvrcCompiler, stripped from VRChat build.
    public class TsvrcConfig : UdonSharpBehaviour
    {
        [Tooltip("Scene objects exposed as named singleton fields on CompiledTsvrc. Drop a GameObject or Component here — each entry becomes a typed _ts.FieldName accessor available to all TsvrcBehaviours.")]
        public Object[] Singletons;
        [Tooltip("Prototype TsvrcBehaviours used as templates for runtime instantiation. Each entry generates a Create<TypeName>() factory method on CompiledTsvrc. The prototype is deactivated at start and cloned on demand.")]
        public TsvrcBehaviour[] TsvrcBehaviourFactory;
        [Tooltip("TsvrcBehaviours that are constructed directly in the scene (not instantiated). TsConstruct(_ts) is called on each during startup instead of a factory method.")]
        public TsvrcBehaviour[] TsvrcBehaviourConstruct;

        [Tooltip("Internal Tsvrc behaviours registered as factory templates. Managed automatically by the Tsvrc compiler — do not assign manually.")]
        public TsvrcBehaviour[] InternalTsvrcBehaviourFactory;
    }
}