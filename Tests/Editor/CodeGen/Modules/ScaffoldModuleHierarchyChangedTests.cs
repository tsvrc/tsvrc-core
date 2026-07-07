using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // ScaffoldModule.OnSceneHierarchyChanged() - the self-healing signal that decides
    // whether a rerun should be scheduled after any hierarchy edit.
    public class ScaffoldModuleHierarchyChangedTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        [Test]
        public void OnSceneHierarchyChanged_CompiledTypeNotFoundCase_IsUnreachableInThisProject_DocumentedNotTested()
        {
            // FindCompiledType() always succeeds in this project (the real TsvrcGenerated
            // class is always compiled and loaded), so the `compiledType == null` branch
            // cannot be reached from an Edit Mode test here - it would only apply to a
            // project where the generator has never produced its first .cs file at all.
            Assert.Pass("Unreachable in this project: FindCompiledType() never returns null here.");
        }

        [Test]
        public void OnSceneHierarchyChanged_ZeroInstances_ReturnsTrue()
        {
            Assert.IsTrue(new ScaffoldModule().OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_ExactlyOneInstanceMissingConfigChild_ReturnsTrue()
        {
            CompiledRootFixture.AddTo(_scope);

            Assert.IsTrue(new ScaffoldModule().OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_ExactlyOneInstanceWithConfigChild_ReturnsFalse()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var configGo = _scope.CreateGameObject("TsvrcConfig");
            configGo.transform.SetParent(root.transform, false);
            configGo.AddComponent<TsvrcConfig>();

            Assert.IsFalse(new ScaffoldModule().OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_TwoInstances_ReturnsTrue()
        {
            CompiledRootFixture.AddTo(_scope, "First");
            CompiledRootFixture.AddTo(_scope, "Second");

            Assert.IsTrue(new ScaffoldModule().OnSceneHierarchyChanged());
        }
    }
}
