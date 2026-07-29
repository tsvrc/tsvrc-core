using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    public class ProcessCleanupTests : ProcessTestBase
    {
        [Test]
        public void BareTsProcess_FullStartStopLifecycle_NoOverridesRequired_DoesNotThrow()
        {
            var process = CreateProcess<Process>();
            SeedAsOwner(process);

            Assert.DoesNotThrow(() =>
            {
                process.StartProcess(useProcessUpdate: true);
                process._TickProcessUpdate();
                process.StopProcess();
            });
            Assert.IsFalse(process.IsProcessRunning());

            // StopProcess's cleanup clears the owner fields; re-seed so this second
            // StartProcess also takes the "already owner" branch instead of touching
            // Networking.
            SeedAsOwner(process);
            Assert.DoesNotThrow(() =>
            {
                process.StartProcess();
                process.CompleteProcess();
            });
            Assert.IsFalse(process.IsProcessRunning());
        }

        [Test]
        public void Stop_NormalCase_ClearsOwnerAndUpdateFields()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess(useProcessUpdate: true);

            process.StopProcess();

            Assert.AreEqual("", PrivateFieldAccess.GetField<string>(process, "_ownerId"));
            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_useProcessUpdate"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
        }

        [Test]
        public void Stop_CallsOnProcessCleanupWithIsCompletedFalse()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            process.StopProcess();

            CollectionAssert.AreEqual(new[] { false }, process.OnProcessCleanupArgs);
        }

        [Test]
        public void Complete_CallsOnProcessCleanupWithIsCompletedTrue()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();

            process.CompleteProcess();

            CollectionAssert.AreEqual(new[] { true }, process.OnProcessCleanupArgs);
        }

        [Test]
        public void Stop_ReentrantStartFromOnProcessStopped_SkipsFieldResetsAndSkipsOldCleanupHook()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);
            process.StartProcess();
            process.CallLog.Clear();
            process.OnProcessStoppedAction = () => process.StartProcess(useProcessUpdate: true);

            process.StopProcess();

            Assert.IsTrue(process.IsProcessRunning(), "Reentrant StartProcess should leave a new process running.");
            Assert.AreEqual(OwnerPlayerId, PrivateFieldAccess.GetField<int>(process, "_ownerPlayerIdInt"),
                "Owner fields must not be cleared out from under the reentrant new process.");
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_useProcessUpdate"));
            CollectionAssert.AreEqual(
                new[] { "OnProcessStopped", "OnProcessStarted" },
                process.CallLog);
            Assert.IsEmpty(process.OnProcessCleanupArgs,
                "OnProcessCleanup for the old process must be skipped after a reentrant start.");
        }
    }
}
