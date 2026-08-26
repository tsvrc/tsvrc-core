using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Utils;

namespace Tsvrc.Tests.EditMode
{
    // LogModule.FindLogger/DetermineNotFoundMessage: pure lookup/messaging helpers TsWindow
    // calls directly so it can own the Log section's SerializedObject/TsPendingConfigEdit
    // lifecycle itself - see TsWindow.DrawLogSection.
    public class LogModuleFindLoggerTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        [Test]
        public void FindLogger_PresentInScene_ReturnsIt()
        {
            var go = _scope.CreateGameObject("TsLogger");
            var logger = go.AddComponent<TsvrcLogger>();

            Assert.AreSame(logger, LogModule.FindLogger());
        }

        [Test]
        public void FindLogger_NotInScene_ReturnsNull()
        {
            Assert.IsNull(LogModule.FindLogger());
        }

        [Test]
        public void DetermineNotFoundMessage_Used_MentionsForceRegenerate()
        {
            // LoadConfig() never ran, so _isUsed defaults to true (TsSingleComponentModule's own
            // default) - the "never generated yet" case.
            StringAssert.Contains("Force Regenerate", new LogModule().DetermineNotFoundMessage());
        }
    }
}
