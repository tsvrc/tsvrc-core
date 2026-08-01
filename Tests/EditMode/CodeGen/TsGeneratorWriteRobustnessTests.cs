using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // WriteModules()/WriteIfChanged() degrade gracefully instead of letting a write failure
    // propagate as an unhandled exception mid-pass, and write each generated file atomically via
    // a temp-file-then-swap so a crash mid-write can never leave a truncated file behind.
    public class TsGeneratorWriteRobustnessTests
    {
        private TsGeneratorTestHarness _harness;
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _harness = new TsGeneratorTestHarness();
            _scope = _harness.Scope;
        }

        [TearDown]
        public void TearDown() => _harness.Dispose();

        [Test]
        public void Run_WriteBlockedForOneModule_LogsErrorAndDoesNotThrow()
        {
            // A directory sitting where SingletonModule's generated file needs to go makes both
            // WriteIfChanged's File.Exists(fullPath) check false (it's a directory, not a file)
            // and the subsequent File.Move throw (a directory already occupies that exact path) -
            // a deterministic, reproducible write failure without touching real OS-level
            // permissions.
            string blockedPath = TsPaths.ToFullPath($"{TsPaths.GeneratedFolder}/TsGeneratedSingleton.cs");
            Directory.CreateDirectory(blockedPath);

            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[Tsvrc\] Failed to write 'TsGeneratedSingleton\.cs'.*"));
                Assert.DoesNotThrow(() => TsGenerator.Run(skipRefresh: true, allowBootstrap: true));
            }
            finally
            {
                // TearDown's harness Dispose() calls TsGenerator.AfterDomainReload(), which runs
                // another real pass - left blocked, that pass would hit the exact same failure a
                // second, unexpected time and fail the test during teardown instead of here.
                Directory.Delete(blockedPath);
            }
        }

        [Test]
        public void Run_WriteBlockedForOneModule_ModulesBeforeItInOrderStillWriteSuccessfully()
        {
            // CreateModules() order: Log, Memory, Pool, Translation, Instance, Singleton, ...
            // Several modules precede Singleton, so this pins that their writes complete and
            // remain valid even though a later module's write fails this same pass: stop
            // attempting further modules, but don't undo what already succeeded.
            string blockedPath = TsPaths.ToFullPath($"{TsPaths.GeneratedFolder}/TsGeneratedSingleton.cs");
            Directory.CreateDirectory(blockedPath);

            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[Tsvrc\] Failed to write 'TsGeneratedSingleton\.cs'.*"));
                TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

                string logPath = TsPaths.ToFullPath($"{TsPaths.GeneratedFolder}/TsGeneratedLog.cs");
                Assert.IsTrue(File.Exists(logPath), "A module ordered before the blocked one must still have its file written.");
            }
            finally
            {
                // Same reasoning as the test above: TearDown's harness Dispose() runs another
                // real pass, which would hit this same failure again if left blocked.
                Directory.Delete(blockedPath);
            }
        }

        [Test]
        public void Run_NormalPass_LeavesNoStrayTempFilesBehind()
        {
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            var tmpFiles = Directory.Exists(TsPaths.GeneratedFolder)
                ? Directory.GetFiles(TsPaths.GeneratedFolder, "*.tmp", SearchOption.AllDirectories)
                : System.Array.Empty<string>();
            CollectionAssert.IsEmpty(tmpFiles, "Atomic writes must never leave a .tmp file behind on the success path.");
        }
    }
}
