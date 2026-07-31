using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Core.Generated;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // Tests ScaffoldModule.OnSceneHierarchyChanged(), the self-healing signal that decides
    // whether a rerun should be scheduled after any hierarchy edit.
    //
    // Redirects TsPaths.CompiledClassName to TestGenerated (permanent, always compiled as
    // part of this test assembly) in SetUp, so every test here is deterministic regardless of
    // whether a consuming project's own TsGenerated happens to be compiled.
    public class ScaffoldModuleHierarchyChangedTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            TsPaths.CompiledClassName = nameof(TestGenerated);
        }

        [TearDown]
        public void TearDown() => _scope.Dispose(); // also resets TsPaths to defaults

        [Test]
        public void OnSceneHierarchyChanged_CompiledTypeNotFound_ReturnsFalse()
        {
            // A name guaranteed not to match any compiled type, genuinely exercising the
            // `compiledType == null` branch. Previously untestable: every test in this file
            // could only ever run against whichever real type happened to already be
            // compiled in the AppDomain, and that's never null once a project has generated
            // anything at all.
            TsPaths.CompiledClassName = "ThisTypeDefinitelyDoesNotExist_Guard12345";

            Assert.IsFalse(new ScaffoldModule().OnSceneHierarchyChanged());
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
            var configGo = _scope.CreateGameObject("TsConfig");
            configGo.transform.SetParent(root.transform, false);
            configGo.AddComponent<TsConfig>();

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
