using System.Collections.Generic;
using Tsvrc.Core;
using Tsvrc.StateMachine;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Plain (non-UdonSharp) MonoBehaviour test doubles carrying [WirePool] fields in every
    // shape PoolModule.ScanExternalRefs()/ScanInternalDeps() need to discriminate: public,
    // serialized-private, non-serialized-private (excluded), array (excluded), and generic
    // (excluded). Kept in TestUtil since both G3 (scan logic) and G4 (Wire()) tests use them.
    public class PoolWireTargetDouble : MonoBehaviour
    {
        [WirePool] public StateManager PublicField;
        [WirePool] [SerializeField] private StateManager _serializedField;
        [WirePool] private StateManager _nonSerializedField;
        [WirePool] public StateManager[] ArrayField;
        [WirePool] public List<StateManager> GenericField;
    }

    // A field declared on a base class must still be discovered by the BaseType walk in
    // both Scan* methods.
    public class PoolWireTargetDoubleBase : MonoBehaviour
    {
        [WirePool] public StateManager BaseClassField;
    }

    public class PoolWireTargetDoubleDerived : PoolWireTargetDoubleBase
    {
    }

    // Isolates the non-serialized-field case with no other [WirePool] fields present, so
    // tests can assert its specific "is not serialized" warning without noise from siblings.
    public class PoolWireOnlyNonSerializedFieldDouble : MonoBehaviour
    {
        [WirePool] private StateManager _nonSerializedField;
    }
}
