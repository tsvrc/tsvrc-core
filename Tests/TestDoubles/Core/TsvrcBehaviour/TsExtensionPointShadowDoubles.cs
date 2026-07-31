using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.StateMachine;
using Tsvrc.Utils;

namespace Tsvrc.Tests.EditMode
{
    // Stands in for a generated shadow class (see ScaffoldModule.GenerateCode()): hides the
    // inherited TsRoot-typed _ts field with a same-named property retyped to a concrete TsRoot
    // subclass (FullTsRootDouble here, TsGenerated in a real project), so every subclass sees
    // _ts as the concrete type with no cast at any call site. The real generated classes can
    // never be referenced from this assembly, since TsGenerated only exists in a consuming
    // project's Assembly-CSharp, so this double is the only way to exercise the mechanism itself.
    public class ExtensionPointBehaviourDouble : TsvrcBehaviour
    {
        protected new FullTsRootDouble _ts => (FullTsRootDouble)base._ts;

        public FullTsRootDouble GetShadowedTs() => _ts;
        public TsRoot GetBaseTs() => base._ts;
    }

    // A leaf world script extending the shadow class, exactly like MolInstance : TsInstance
    // or SpawnManager : TsBehaviour extend theirs. Proves ordinary _ts.Member access
    // reaches members that exist only on the concrete root, not on TsRoot.
    public class ExtensionPointBehaviourLeafDouble : ExtensionPointBehaviourDouble
    {
        public Instance ReadInstanceThroughShadow() => _ts.FakeInstance;
    }

    // Mirrors TsStateManager : StateManager (and TsInstance : Instance, TsListItem : ListItem,
    // TsProcess : Process): a Tsvrc framework "extension point" class sits between
    // TsvrcBehaviour and the shadow property.
    public class ExtensionPointStateManagerDouble : StateManager
    {
        protected new FullTsRootDouble _ts => (FullTsRootDouble)base._ts;
    }

    public class ExtensionPointStateManagerLeafDouble : ExtensionPointStateManagerDouble
    {
        public TsvrcMemory ReadMemoryThroughShadow() => _ts.FakeMemory;
    }
}
