using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    public class ProcessDeserializationTests : ProcessTestBase
    {
        [Test]
        public void OnDeserialization_OwnerAndRunningButLoopNotActive_RestartsTheLoop()
        {
            // Simulates the stale-packet-recovery case this override exists for: a reliable
            // packet resets _isRunning to true after the loop had already died.
            var process = CreateProcess<ProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_isRunning", true);
            PrivateFieldAccess.SetField(process, "_updateLoopActive", false);

            process.OnDeserialization();

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }

        [Test]
        public void OnDeserialization_NotRunning_DoesNotStartTheLoop()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_isRunning", false);

            process.OnDeserialization();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }
    }
}
