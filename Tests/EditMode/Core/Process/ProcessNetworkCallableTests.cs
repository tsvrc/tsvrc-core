using System.Reflection;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UdonSharp;
using UnityEngine;
using UnityEngine.TestTools;

using Tsvrc.Tests.Doubles;

namespace Tsvrc.Tests.EditMode
{
    // The "rejected because not the owner" half of each guard can't be reached here (see
    // ProcessTestBase). It's covered in Tests/PlayMode/Core/Process/ instead.
    public class ProcessNetworkCallableTests : ProcessTestBase
    {
        private static int CurrentGeneration(Tsvrc.Core.Process process)
        {
            return PrivateFieldAccess.GetField<int>(process, "_runGeneration");
        }

        [Test]
        public void RunGeneration_IsDeclaredUdonSynced()
        {
            FieldInfo field = typeof(Tsvrc.Core.Process).GetField("_runGeneration",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "_runGeneration field not found. Renamed?");
            Assert.IsNotNull(field.GetCustomAttribute<UdonSyncedAttribute>(),
                "_runGeneration must be [UdonSynced] or it never reaches other clients.");
        }

        [Test]
        public void RequestStartProcess_NotRunning_Executes()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            process.RequestStartProcess();

            Assert.AreEqual(1, process.OnProcessStartedCount);
        }

        [Test]
        public void RequestStartProcess_AlreadyRunning_RejectedAndLogsWarning()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            LogAssert.Expect(LogType.Warning,
                "[TsVRC] [ProcessTestSubclass] RequestStartProcess rejected: not the owner or already running.");
            process.RequestStartProcess();

            Assert.AreEqual(1, process.OnProcessStartedCount);
        }

        [Test]
        public void RequestStopProcess_OwnerRunningAndCurrentGeneration_Executes()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            process.RequestStopProcess(CurrentGeneration(process));

            Assert.AreEqual(1, process.OnProcessStoppedCount);
        }

        [Test]
        public void RequestStopProcess_NotRunning_RejectedAndLogsWarning()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            LogAssert.Expect(LogType.Warning,
                "[TsVRC] [ProcessTestSubclass] RequestStopProcess rejected: not running.");
            process.RequestStopProcess(CurrentGeneration(process));

            Assert.AreEqual(0, process.OnProcessStoppedCount);
        }

        [Test]
        public void RequestStopProcess_StaleGeneration_RejectedAndLogsWarning()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess(); // run 1
            int staleGeneration = CurrentGeneration(process);
            process.StopProcess(); // run 1 ends
            process.StartProcess(); // run 2 - a new, unrelated run, same owner

            LogAssert.Expect(LogType.Warning,
                "[TsVRC] [ProcessTestSubclass] RequestStopProcess rejected: stale run.");
            process.RequestStopProcess(staleGeneration); // run 1's delayed request finally arrives

            Assert.IsTrue(process.IsProcessRunning(),
                "A stale request for a run that already ended must not stop whatever run is current now.");
        }

        [Test]
        public void RequestCompleteProcess_OwnerRunningAndCurrentGeneration_Executes()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            process.RequestCompleteProcess(CurrentGeneration(process));

            Assert.AreEqual(1, process.OnProcessCompletedCount);
        }

        [Test]
        public void RequestCompleteProcess_NotRunning_RejectedAndLogsWarning()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            LogAssert.Expect(LogType.Warning,
                "[TsVRC] [ProcessTestSubclass] RequestCompleteProcess rejected: not running.");
            process.RequestCompleteProcess(CurrentGeneration(process));

            Assert.AreEqual(0, process.OnProcessCompletedCount);
        }

        [Test]
        public void RequestCompleteProcess_StaleGeneration_RejectedAndLogsWarning()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();
            int staleGeneration = CurrentGeneration(process);
            process.CompleteProcess();
            process.StartProcess();

            LogAssert.Expect(LogType.Warning,
                "[TsVRC] [ProcessTestSubclass] RequestCompleteProcess rejected: stale run.");
            process.RequestCompleteProcess(staleGeneration);

            Assert.IsTrue(process.IsProcessRunning(),
                "A stale request for a run that already ended must not complete whatever run is current now.");
        }
    }
}
