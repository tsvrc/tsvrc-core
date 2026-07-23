using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode.Testing.Framework
{
    // Proves BatchModeTerminationFixup actually terminates the run the right way for each
    // mode (real batch-mode exit code vs. dropping out of interactive Play Mode). Uses the
    // fixup's internal seams (IsBatchMode/ExitBatchMode/StopPlaying), made visible here via
    // InternalsVisibleTo in Testing/Framework/AssemblyInfo.cs, so this test never actually
    // exits the process or Play Mode itself.
    public class BatchModeTerminationFixupTests
    {
        [Test]
        public void OnAfterAllTests_InBatchModeAndPassed_ExitsWithCodeZero()
        {
            var fixup = new BatchModeTerminationFixup { IsBatchMode = () => true };
            int? exitCode = null;
            bool stopPlayingCalled = false;
            fixup.ExitBatchMode = code => exitCode = code;
            fixup.StopPlaying = () => stopPlayingCalled = true;

            fixup.OnAfterAllTests(true);

            Assert.AreEqual(0, exitCode);
            Assert.IsFalse(stopPlayingCalled);
        }

        [Test]
        public void OnAfterAllTests_InBatchModeAndFailed_ExitsWithCodeOne()
        {
            var fixup = new BatchModeTerminationFixup { IsBatchMode = () => true };
            int? exitCode = null;
            bool stopPlayingCalled = false;
            fixup.ExitBatchMode = code => exitCode = code;
            fixup.StopPlaying = () => stopPlayingCalled = true;

            fixup.OnAfterAllTests(false);

            Assert.AreEqual(1, exitCode);
            Assert.IsFalse(stopPlayingCalled);
        }

        [Test]
        public void OnAfterAllTests_NotInBatchMode_StopsPlayingInstead()
        {
            var fixup = new BatchModeTerminationFixup { IsBatchMode = () => false };
            bool exitCalled = false;
            bool stopPlayingCalled = false;
            fixup.ExitBatchMode = _ => exitCalled = true;
            fixup.StopPlaying = () => stopPlayingCalled = true;

            fixup.OnAfterAllTests(true);

            Assert.IsFalse(exitCalled);
            Assert.IsTrue(stopPlayingCalled);
        }
    }
}
