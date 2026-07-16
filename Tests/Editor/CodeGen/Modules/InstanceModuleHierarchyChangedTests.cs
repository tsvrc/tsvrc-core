using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // InstanceModule.OnSceneHierarchyChanged().
    public class InstanceModuleHierarchyChangedTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static InstanceModule ModuleWith(System.Type detectedType, bool ambiguous)
        {
            var module = new InstanceModule();
            PrivateFieldAccess.SetField(module, "_detectedType", detectedType);
            PrivateFieldAccess.SetField(module, "_ambiguous", ambiguous);
            return module;
        }

        [Test]
        public void OnSceneHierarchyChanged_Ambiguous_AlwaysReturnsFalse()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            // Even with a root and no TsInstance child present, ambiguous must never
            // self-trigger - it's the one deliberate "leave broken things alone" branch.
            var module = ModuleWith(typeof(object), ambiguous: true);

            Assert.IsFalse(module.OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_NoDetectedType_ReturnsFalse()
        {
            CompiledRootFixture.AddTo(_scope);
            var module = ModuleWith(null, ambiguous: false);

            Assert.IsFalse(module.OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_RootAbsent_ReturnsFalse()
        {
            var module = ModuleWith(typeof(object), ambiguous: false);

            Assert.IsFalse(module.OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_RootPresentChildAbsent_ReturnsTrue()
        {
            CompiledRootFixture.AddTo(_scope);
            var module = ModuleWith(typeof(object), ambiguous: false);

            Assert.IsTrue(module.OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_RootPresentChildPresent_ReturnsFalse()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var child = _scope.CreateGameObject("TsInstance");
            child.transform.SetParent(root.transform, false);
            var module = ModuleWith(typeof(object), ambiguous: false);

            Assert.IsFalse(module.OnSceneHierarchyChanged());
        }
    }
}
