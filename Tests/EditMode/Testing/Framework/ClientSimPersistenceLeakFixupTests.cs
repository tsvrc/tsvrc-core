using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine;
using VRC.SDK3.ClientSim.Persistence;

namespace Tsvrc.Tests.EditMode.Testing.Framework
{
    // Proves ClientSimPersistenceLeakFixup actually destroys a leaked ClientSimPlayerObjectStorage
    // (the real ClientSim leak this fixup exists to clean up).
    public class ClientSimPersistenceLeakFixupTests
    {
        [Test]
        public void OnUnityTearDown_WhenLeakedStorageExists_DestroysIt()
        {
            var leaked = new GameObject("LeakedClientSimPlayerObjectStorage");
            leaked.AddComponent<ClientSimPlayerObjectStorage>();
            leaked.hideFlags = HideFlags.HideInHierarchy;

            new ClientSimPersistenceLeakFixup().OnUnityTearDown();

            Assert.IsTrue(leaked == null, "Leaked ClientSimPlayerObjectStorage was not destroyed.");
        }

        [Test]
        public void OnUnityTearDown_WhenNoLeakExists_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => new ClientSimPersistenceLeakFixup().OnUnityTearDown());
        }
    }
}
