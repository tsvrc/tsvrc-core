using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    public class ProcessIntegrationTests : ProcessTestBase
    {
        [Test]
        public void BareProcess_FullStartStopLifecycle_NoOverridesRequired_DoesNotThrow()
        {
            var process = CreateProcess<Process>();

            Assert.DoesNotThrow(() =>
            {
                process.StartProcess(useProcessUpdate: true);
                process._TickProcessUpdate();
                process.StopProcess();
            });
            Assert.IsFalse(process.IsProcessRunning());

            Assert.DoesNotThrow(() =>
            {
                process.StartProcess();
                process.CompleteProcess();
            });
            Assert.IsFalse(process.IsProcessRunning());
        }

        [Test]
        public void GoldenPath_StartWithUpdatesThenComplete_FullHookOrderAndFinalState()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            process.StartProcess(useProcessUpdate: true);
            process._TickProcessUpdate();
            ForceNextTickDueNow(process);
            process._TickProcessUpdate();
            ForceNextTickDueNow(process);
            process._TickProcessUpdate();
            process.CompleteProcess();

            CollectionAssert.AreEqual(
                new[]
                {
                    "OnProcessStarted",
                    "OnProcessUpdate",
                    "OnProcessUpdate",
                    "OnProcessUpdate",
                    "OnProcessCompleted",
                    "OnProcessCleanup:True",
                },
                process.CallLog);
            Assert.IsFalse(process.IsProcessRunning());
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_ownershipEstablished"));
        }

        [Test]
        public void GoldenPath_StartThenStopBeforeCompletion_OnlyStoppedHookFires()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            process.StartProcess();
            process.StopProcess();

            Assert.AreEqual(1, process.OnProcessStoppedCount);
            Assert.AreEqual(0, process.OnProcessCompletedCount);
            CollectionAssert.AreEqual(
                new[] { "OnProcessStarted", "OnProcessStopped", "OnProcessCleanup:False" },
                process.CallLog);
        }

        [Test]
        public void ProcessSubclass_CanUseInheritedPubSub_FromWithinItsOwnHook()
        {
            var process = CreateProcess<EventingProcess>();
            var listenerDouble = CreateComponent<TsListenerDouble>("Listener");
            process.TsSubscribe(listenerDouble, EventingProcess.DoneEvent, nameof(TsListenerDouble.CallbackA));

            process.StartProcess();
            process.CompleteProcess();

            Assert.AreEqual(1, listenerDouble.CallbackACount);
        }
    }
}
