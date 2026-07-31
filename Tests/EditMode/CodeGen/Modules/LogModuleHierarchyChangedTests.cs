using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Utils;

namespace Tsvrc.Tests.EditMode
{
    // LogModule.OnSceneHierarchyChanged(). Mirrors MemoryModuleHierarchyChangedTests.
    public class LogModuleHierarchyChangedTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        [Test]
        public void OnSceneHierarchyChanged_RootAbsent_ReturnsFalse()
        {
            Assert.IsFalse(new LogModule().OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_RootPresentTsLoggerChildAbsent_ReturnsTrue()
        {
            CompiledRootFixture.AddTo(_scope);

            Assert.IsTrue(new LogModule().OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_RootPresentTsLoggerChildPresent_ReturnsFalse()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var child = _scope.CreateGameObject("TsLogger");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<TsvrcLogger>();

            Assert.IsFalse(new LogModule().OnSceneHierarchyChanged());
        }
    }
}
