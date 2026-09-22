using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

using Tsvrc.Tests.Doubles;

namespace Tsvrc.Tests.EditMode
{
    public class ProcessLifecycleTests : ProcessTestBase
    {
        [Test]
        public void IsProcessRunning_ReflectsIsRunningFieldAcrossStates()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            Assert.IsFalse(process.IsProcessRunning());

            PrivateFieldAccess.SetField(process, "_isRunning", true);
            Assert.IsTrue(process.IsProcessRunning());

            PrivateFieldAccess.SetField(process, "_isRunning", false);
            Assert.IsFalse(process.IsProcessRunning());
        }

        [Test]
        public void StartProcess_Fresh_SetsIsRunningTrueAndFiresOnProcessStarted()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            process.StartProcess();

            Assert.IsTrue(process.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "OnProcessStarted" }, process.CallLog);
        }

        [Test]
        public void StartProcess_AlreadyRunning_IsNoOpAndLogsWarning()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            LogAssert.Expect(LogType.Warning,
                "[TsVRC] [ProcessTestSubclass] Process is already running, ignoring start call.");
            process.StartProcess();

            Assert.AreEqual(1, process.OnProcessStartedCount);
        }

        [Test]
        public void StartProcess_AfterStopProcess_RestartsCleanly()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess(useProcessUpdate: true);
            process.StopProcess();
            Assert.IsFalse(process.IsProcessRunning());

            process.StartProcess();

            Assert.IsTrue(process.IsProcessRunning());
            Assert.AreEqual(2, process.OnProcessStartedCount);
        }

        [Test]
        public void StopProcess_NotRunning_IsNoOpAndLogsWarning()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            LogAssert.Expect(LogType.Warning,
                "[TsVRC] [ProcessTestSubclass] Process is not running, ignoring stop call.");
            process.StopProcess();

            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [Test]
        public void StopProcess_Owner_ExecutesStopDirectly()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            process.StopProcess();

            Assert.AreEqual(1, process.OnProcessStoppedCount);
            Assert.AreEqual(0, process.OnProcessCompletedCount);
            Assert.IsFalse(process.IsProcessRunning());
        }

        [Test]
        public void StopProcess_IsRunningFalseBeforeOnProcessStoppedFires()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            process.StopProcess();

            Assert.IsTrue(process.RunningStateInsideOnProcessStopped.HasValue);
            Assert.IsFalse(process.RunningStateInsideOnProcessStopped.Value);
        }

        [Test]
        public void CompleteProcess_NotRunning_IsNoOpAndLogsWarning()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            LogAssert.Expect(LogType.Warning,
                "[TsVRC] [ProcessTestSubclass] Process is not running, ignoring complete call.");
            process.CompleteProcess();

            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }

        [Test]
        public void CompleteProcess_Owner_ExecutesCompleteDirectly()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            process.CompleteProcess();

            Assert.AreEqual(1, process.OnProcessCompletedCount);
            Assert.AreEqual(0, process.OnProcessStoppedCount);
            Assert.IsFalse(process.IsProcessRunning());
        }

        [Test]
        public void CompleteProcess_IsRunningFalseBeforeOnProcessCompletedFires()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            process.CompleteProcess();

            Assert.IsTrue(process.RunningStateInsideOnProcessCompleted.HasValue);
            Assert.IsFalse(process.RunningStateInsideOnProcessCompleted.Value);
        }
    }
}
