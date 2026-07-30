using Tsvrc.Core;
using Tsvrc.Utils;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core.Generated
{
    // Permanent stand-in for a consuming project's real, per-project generated TsGenerated
    // type. CodeGen module Wire() and Run() tests redirect TsPaths.CompiledClassName to this
    // type's name, see CompiledRootFixture, so ScaffoldModule.FindCompiledType() resolves to
    // it instead of hunting the AppDomain for whatever the host project happens to have
    // generated. The suite is then deterministic on a fresh clone with nothing generated
    // yet, exactly as it is on a fully bootstrapped project.
    //
    // Lives in the same namespace as the real generated type, Tsvrc.Core.Generated, but under
    // a different class name, TestGenerated rather than TsGenerated, so the two can never
    // collide as duplicate type definitions even when both are loaded in the same AppDomain.
    // That is always true in editor, since this test assembly and a consuming project's
    // Assembly-CSharp are both loaded together.
    //
    // Deliberately not trying to mirror any specific module's real generated field shape.
    // Wire()-level tests that need one particular field name build their own small, dedicated
    // double instead, see for example FactoryModuleWireTests.FactoryFieldDouble, rather than
    // coupling to this type's exact fields. GenericSlotA and GenericSlotB exist only for tests
    // that want some real field of a known name and type already on the compiled root, without
    // pretending it's a specific module's own output, see SingletonModuleWireTests.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TestGenerated : TsRoot
    {
        [SerializeField] private TsMemory _memory;
        [SerializeField] private TsLogger _log;
        [SerializeField] private Instance _instance;
        [SerializeField] public UdonSharpBehaviour GenericSlotA;
        [SerializeField] public UdonSharpBehaviour GenericSlotB;
        [SerializeField] public GameObject FactorySlotA;

        public override TsMemory Memory => _memory;
        public override TsLogger Log => _log;
        public override Instance Instance => _instance;
    }
}
