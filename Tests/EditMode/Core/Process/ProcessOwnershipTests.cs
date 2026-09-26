using NUnit.Framework;
using Tsvrc.Testing.Framework;

using Tsvrc.Tests.Doubles;

namespace Tsvrc.Tests.EditMode
{
    // Canary for the assumption every other EditMode Process test relies on: see
    // ProcessTestBase's class comment. If this ever starts failing, every other EditMode
    // test in this suite needs to move to Play Mode.
    public class ProcessOwnershipTests : ProcessTestBase
    {
        [Test]
        public void IsProcessOwner_BareGameObjectOutsidePlayMode_DefaultsTrue()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            Assert.IsTrue((bool)PrivateFieldAccess.InvokeInstance(process, "IsProcessOwner"));
        }
    }
}
