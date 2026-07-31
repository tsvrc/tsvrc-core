using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // TsDomainReloadHandler.IsRunningAutomatedTests() decides whether the automatic
    // post-domain-reload trigger should skip TsGenerator.AfterDomainReload() entirely. See its
    // own doc comment for why: HasBootstrapSignal() reacts to a loaded Instance subclass, not
    // scene content, so this is the only thing that stops the automatic trigger from running
    // for real against whatever scene the NUnit batchmode test runner happens to have open and
    // overwriting a consuming project's real generated files with an almost-empty result.
    public class TsDomainReloadHandlerTests
    {
        [Test]
        public void IsRunningAutomatedTests_RunTestsFlagPresent_ReturnsTrue()
        {
            Assert.IsTrue(TsDomainReloadHandler.IsRunningAutomatedTests(
                new[] { "Unity.exe", "-batchmode", "-runTests", "-testPlatform", "EditMode" }));
        }

        [Test]
        public void IsRunningAutomatedTests_RunTestsFlagPresent_IsCaseInsensitive()
        {
            Assert.IsTrue(TsDomainReloadHandler.IsRunningAutomatedTests(new[] { "-RUNTESTS" }));
        }

        [Test]
        public void IsRunningAutomatedTests_NoRunTestsFlag_ReturnsFalse()
        {
            Assert.IsFalse(TsDomainReloadHandler.IsRunningAutomatedTests(
                new[] { "Unity.exe", "-batchmode", "-quit" }));
        }

        [Test]
        public void IsRunningAutomatedTests_EmptyArgs_ReturnsFalse()
        {
            Assert.IsFalse(TsDomainReloadHandler.IsRunningAutomatedTests(new string[0]));
        }
    }
}
