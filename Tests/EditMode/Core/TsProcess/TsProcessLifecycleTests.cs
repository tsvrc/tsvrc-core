using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    public class TsProcessLifecycleTests : TsProcessTestBase
    {
        [Test]
        public void IsProcessRunning_ReflectsIsRunningFieldAcrossStates()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            Assert.IsFalse(process.IsProcessRunning());

            PrivateFieldAccess.SetField(process, "_isRunning", true);
            Assert.IsTrue(process.IsProcessRunning());

            PrivateFieldAccess.SetField(process, "_isRunning", false);
            Assert.IsFalse(process.IsProcessRunning());
        }

        [Test]
        public void StartProcess_Fresh_SetsIsRunningTrue()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            process.StartProcess();

            Assert.IsTrue(process.IsProcessRunning());
        }

        [Test]
        public void StartProcess_DefaultUseProcessUpdateFalse_NoTickLoopScheduled()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            process.StartProcess();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_useProcessUpdate"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
            Assert.AreEqual(0, process.OnProcessUpdateCount);
        }

        [Test]
        public void StartProcess_UseProcessUpdateTrue_ActivatesLoopButDoesNotTickSynchronously()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            process.StartProcess(useProcessUpdate: true);

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_useProcessUpdate"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
            Assert.AreEqual(0, process.OnProcessUpdateCount, "First tick must be deferred, not synchronous.");

            // Simulate the deferred SendCustomEventDelayedSeconds(..., 0f) callback firing.
            process._TickProcessUpdate();
            Assert.AreEqual(1, process.OnProcessUpdateCount);
        }

        [Test]
        public void StartProcess_AlreadyRunning_IsNoOpAndLogsWarning()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();
            Assert.AreEqual(1, process.OnProcessStartedCount);

            LogAssert.Expect(LogType.Warning, "[TsProcess] Process is already running.");
            process.StartProcess();

            Assert.AreEqual(1, process.OnProcessStartedCount);
        }

        [Test]
        public void StartProcess_AlreadyOwner_CallsRequestSerializationBranch_NotSetProcessOwnerBranch()
        {
            // RequestSerialization() is an unobservable no-op stub in the Editor proxy,
            // so this test only pins down which branch StartProcess takes, not any
            // effect of the call itself.
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            Assert.DoesNotThrow(() => process.StartProcess());
            Assert.IsTrue(process.IsProcessRunning());
        }

        [Test]
        public void StartProcess_OnProcessStarted_FiresAfterOwnerBranch_BeforeTickScheduled()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            process.StartProcess(useProcessUpdate: true);

            CollectionAssert.AreEqual(new[] { "OnProcessStarted" }, process.CallLog);
        }

        [Test]
        public void StartProcess_AfterStopProcess_RestartsCleanly()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess(useProcessUpdate: true);
            process.StopProcess();
            Assert.IsFalse(process.IsProcessRunning());

            // StopProcess's cleanup clears _ownerId/_ownerPlayerIdInt, so IsProcessOwner()
            // is false again; re-seed before restarting to stay on the owner branch.
            SeedAsOwner(process);
            process.StartProcess();

            Assert.IsTrue(process.IsProcessRunning());
            Assert.AreEqual(2, process.OnProcessStartedCount);
        }

        [Test]
        public void StopProcess_NotRunning_IsNoOpAndLogsWarning()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            LogAssert.Expect(LogType.Warning, "[TsProcess] Process is not running.");
            process.StopProcess();

            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [Test]
        public void StopProcess_Owner_ExecutesStopDirectly()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            process.StopProcess();

            Assert.AreEqual(1, process.OnProcessStoppedCount);
            Assert.AreEqual(0, process.OnProcessCompletedCount);
            Assert.IsFalse(process.IsProcessRunning());
        }

        [Test]
        public void StopProcess_IsRunningFalseBeforeOnProcessStoppedFires()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            process.StopProcess();

            Assert.IsTrue(process.RunningStateInsideOnProcessStopped.HasValue);
            Assert.IsFalse(process.RunningStateInsideOnProcessStopped.Value);
        }

        [Test]
        public void CompleteProcess_NotRunning_IsNoOpAndLogsWarning()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            LogAssert.Expect(LogType.Warning, "[TsProcess] Process is not running.");
            process.CompleteProcess();

            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }

        [Test]
        public void CompleteProcess_Owner_ExecutesCompleteDirectly()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            process.CompleteProcess();

            Assert.AreEqual(1, process.OnProcessCompletedCount);
            Assert.AreEqual(0, process.OnProcessStoppedCount);
            Assert.IsFalse(process.IsProcessRunning());
        }

        [Test]
        public void CompleteProcess_IsRunningFalseBeforeOnProcessCompletedFires()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            process.CompleteProcess();

            Assert.IsTrue(process.RunningStateInsideOnProcessCompleted.HasValue);
            Assert.IsFalse(process.RunningStateInsideOnProcessCompleted.Value);
        }
    }
}
