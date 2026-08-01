using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.DetermineAmbiguousConfigWarning is the pure decision behind RunCore's loud,
    // specific "multiple TsConfig found" diagnostic, mirroring InstanceModule's own "multiple
    // subclasses found" pattern instead of silently picking whichever one Unity's internal
    // iteration order happens to return first.
    public class TsGeneratorConfigAmbiguityTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        [Test]
        public void ZeroFound_ReturnsNull()
        {
            Assert.IsNull(TsGenerator.DetermineAmbiguousConfigWarning(new List<TsConfig>()));
        }

        [Test]
        public void Null_ReturnsNull()
        {
            Assert.IsNull(TsGenerator.DetermineAmbiguousConfigWarning(null));
        }

        [Test]
        public void ExactlyOneFound_ReturnsNull()
        {
            var config = _scope.CreateGameObject("TsConfig").AddComponent<TsConfig>();

            Assert.IsNull(TsGenerator.DetermineAmbiguousConfigWarning(new List<TsConfig> { config }));
        }

        [Test]
        public void TwoFound_NamesBothHierarchyPaths()
        {
            var root = _scope.CreateGameObject("TsGenerated");
            var first = _scope.CreateGameObject("TsConfig");
            first.transform.SetParent(root.transform, false);
            var configA = first.AddComponent<TsConfig>();
            var second = _scope.CreateGameObject("TsConfig");
            var configB = second.AddComponent<TsConfig>();

            string message = TsGenerator.DetermineAmbiguousConfigWarning(new List<TsConfig> { configA, configB });

            StringAssert.Contains("Multiple TsConfig components found", message);
            StringAssert.Contains("TsGenerated/TsConfig", message);
            StringAssert.Contains("Exactly one is required", message);
        }

        [Test]
        public void ThreeFound_ListsAllThree()
        {
            var a = _scope.CreateGameObject("A").AddComponent<TsConfig>();
            var b = _scope.CreateGameObject("B").AddComponent<TsConfig>();
            var c = _scope.CreateGameObject("C").AddComponent<TsConfig>();

            string message = TsGenerator.DetermineAmbiguousConfigWarning(new List<TsConfig> { a, b, c });

            StringAssert.Contains("A", message);
            StringAssert.Contains("B", message);
            StringAssert.Contains("C", message);
        }
    }
}
