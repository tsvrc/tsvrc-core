using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    public class TsProcessNetworkCallableTests : TsProcessTestBase
    {
        [Test]
        public void RequestStopProcess_OwnerAndRunning_Executes()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            process.RequestStopProcess();

            Assert.AreEqual(1, process.OnProcessStoppedCount);
        }

        [Test]
        public void RequestStopProcess_NotRunning_RejectedEvenThoughOwner()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            process.RequestStopProcess();

            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [Test]
        public void RequestCompleteProcess_OwnerAndRunning_Executes()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            process.RequestCompleteProcess();

            Assert.AreEqual(1, process.OnProcessCompletedCount);
        }

        [Test]
        public void RequestCompleteProcess_NotRunning_RejectedEvenThoughOwner()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            process.RequestCompleteProcess();

            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }

        [Test]
        public void RequestStopProcess_StaleCallArrivesAfterSameOwnerRestartsProcess_IncorrectlyStopsTheNewRun()
        {
            // Pins the current, documented gap rather than asserting a "fixed" behavior —
            // matching TwoIndependentClients_...'s own pinning of the two-client StartProcess
            // race for the same reason.
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess(); // run 1
            process.StopProcess(); // run 1 ends; the stale request below conceptually belongs here
            SeedAsOwner(process);
            process.StartProcess(); // run 2 — a new, unrelated run, same owner

            process.RequestStopProcess(); // run 1's delayed request finally arrives

            Assert.IsFalse(process.IsProcessRunning(),
                "Documents the gap: a stale request for a past run stops the new run instead of being discarded.");
        }

        [Test]
        public void RequestCompleteProcess_StaleCallArrivesAfterSameOwnerRestartsProcess_IncorrectlyCompletesTheNewRun()
        {
            // Same gap as RequestStopProcess above, mirrored for RequestCompleteProcess.
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();
            process.CompleteProcess();
            SeedAsOwner(process);
            process.StartProcess();

            process.RequestCompleteProcess();

            Assert.IsFalse(process.IsProcessRunning(),
                "Documents the gap: a stale request for a past run completes the new run instead of being discarded.");
        }
    }
}
