using NUnit.Framework;
using Tsvrc.Testing.Framework;

using Tsvrc.Tests.Doubles;

namespace Tsvrc.Tests.EditMode
{
    // Only the "not running" half of the guard is reachable here; owner is always true (see
    // ProcessTestBase). The "not owner" half lives in Tests/PlayMode/Core/Process/.
    public class ProcessOwnershipTransferredTests : ProcessTestBase
    {
        [Test]
        public void OnOwnershipTransferred_NotRunning_ReturnsWithoutTakingOver()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            InvokeVrcPlayerCallback(process, "OnOwnershipTransferred");

            Assert.AreEqual(0, process.OnBecameProcessOwnerCount);
        }

        [Test]
        public void OnOwnershipTransferred_RunningButOwnershipAlreadyEstablished_ReturnsWithoutTakingOver()
        {
            // Tests TakeOverRunningProcess's own dedup guard, since IsProcessOwner() is always true here.
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            InvokeVrcPlayerCallback(process, "OnOwnershipTransferred");

            Assert.AreEqual(0, process.OnBecameProcessOwnerCount);
        }

        [Test]
        public void OnOwnershipTransferred_RunningAndOwnershipNotYetEstablished_TakesOver()
        {
            // _isRunning set directly (not via StartProcess) so _ownershipEstablished stays false,
            // simulating a process left running by whoever owned it before.
            var process = CreateProcess<ProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_isRunning", true);

            InvokeVrcPlayerCallback(process, "OnOwnershipTransferred");

            Assert.AreEqual(1, process.OnBecameProcessOwnerCount);
        }
    }
}
