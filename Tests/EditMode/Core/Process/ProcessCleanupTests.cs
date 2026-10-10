using NUnit.Framework;
using Tsvrc.Testing.Framework;

using Tsvrc.Tests.Doubles;

namespace Tsvrc.Tests.EditMode
{
    public class ProcessCleanupTests : ProcessTestBase
    {
        [Test]
        public void Stop_NormalCase_ClearsUpdateAndOwnershipFields()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess(useProcessUpdate: true);

            process.StopProcess();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_useProcessUpdate"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_updateLoopActive"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_ownershipEstablished"));
        }

        [Test]
        public void Stop_CallsOnProcessCleanupWithIsCompletedFalse()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            process.StopProcess();

            CollectionAssert.AreEqual(new[] { false }, process.OnProcessCleanupArgs);
        }

        [Test]
        public void Complete_CallsOnProcessCleanupWithIsCompletedTrue()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();

            process.CompleteProcess();

            CollectionAssert.AreEqual(new[] { true }, process.OnProcessCleanupArgs);
        }

        [Test]
        public void Stop_ReentrantStartFromOnProcessStopped_SkipsFieldResetsAndSkipsOldCleanupHook()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            process.StartProcess();
            process.CallLog.Clear();
            process.OnProcessStoppedAction = () => process.StartProcess(useProcessUpdate: true);

            process.StopProcess();

            Assert.IsTrue(process.IsProcessRunning(), "Reentrant StartProcess should leave a new process running.");
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_useProcessUpdate"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(process, "_ownershipEstablished"),
                "Ownership fields must not be cleared out from under the reentrant new process.");
            CollectionAssert.AreEqual(
                new[] { "OnProcessStopped", "OnProcessStarted" },
                process.CallLog);
            Assert.IsEmpty(process.OnProcessCleanupArgs,
                "OnProcessCleanup for the old process must be skipped after a reentrant start.");
        }
    }
}
