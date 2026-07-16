using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Utils;

namespace Tsvrc.Tests.Editor
{
    // MemoryModule.OnSceneHierarchyChanged().
    public class MemoryModuleHierarchyChangedTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        [Test]
        public void OnSceneHierarchyChanged_RootAbsent_ReturnsFalse()
        {
            Assert.IsFalse(new MemoryModule().OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_RootPresentTsMemoryChildAbsent_ReturnsTrue()
        {
            CompiledRootFixture.AddTo(_scope);

            Assert.IsTrue(new MemoryModule().OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_RootPresentTsMemoryChildPresent_ReturnsFalse()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var child = _scope.CreateGameObject("TsMemory");
            child.transform.SetParent(root.transform, false);
            child.AddComponent<TsMemory>();

            Assert.IsFalse(new MemoryModule().OnSceneHierarchyChanged());
        }
    }
}
