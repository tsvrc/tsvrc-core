using NUnit.Framework;
using Tsvrc.Testing.Framework;

using Tsvrc.Tests.Doubles;

namespace Tsvrc.Tests.EditMode
{
    // Owner is always true here, so only the "not running" branch is reachable. The "not owner"
    // branch lives in Tests/PlayMode/Core/Process/ instead.
    public class ProcessPlayerLeftTests : ProcessTestBase
    {
        [Test]
        public void OnPlayerLeft_NotRunning_ReturnsWithoutTakingOver()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            InvokeVrcPlayerCallback(process, "OnPlayerLeft");

            Assert.AreEqual(0, process.OnBecameProcessOwnerCount);
        }

        [Test]
        public void OnPlayerLeft_RunningButOwnershipAlreadyEstablished_ReturnsWithoutTakingOver()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            InvokeVrcPlayerCallback(process, "OnPlayerLeft");

            Assert.AreEqual(0, process.OnBecameProcessOwnerCount);
        }

        [Test]
        public void OnPlayerLeft_RunningAndOwnershipNotYetEstablished_TakesOver()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_isRunning", true);

            InvokeVrcPlayerCallback(process, "OnPlayerLeft");

            Assert.AreEqual(1, process.OnBecameProcessOwnerCount);
        }
    }
}
