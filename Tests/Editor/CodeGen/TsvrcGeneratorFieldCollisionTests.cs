using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // TsvrcGenerator.DetectAndExcludeFieldNameCollisions() - extracted out of Run() purely
    // for testability (CODEGEN_TESTING_PLAN.md Phase G6.3). Among the real 8 modules, only
    // SingletonModule currently overrides ExposedFieldNames()/ExcludeFieldNames(), and its
    // own Resolve()/Deduplicate() step already prevents same-module collisions before this
    // ever runs - so the cross-module path is only reachable through synthetic modules like
    // these, confirming the mechanism itself still works even though no real module
    // combination currently trips it.
    public class TsvrcGeneratorFieldCollisionTests
    {
        private class RecordingModule : TsvrcModule
        {
            private readonly string[] _exposedNames;
            internal List<string> ExcludedNames { get; } = new List<string>();

            internal RecordingModule(params string[] exposedNames) => _exposedNames = exposedNames;

            internal override void LoadConfig() { }
            internal override IEnumerable<string> ExposedFieldNames() => _exposedNames;
            internal override void ExcludeFieldNames(IEnumerable<string> names) => ExcludedNames.AddRange(names);
        }

        [Test]
        public void TwoModulesExposeSameName_BothReceiveItViaExcludeFieldNames()
        {
            var a = new RecordingModule("Foo", "Unique1");
            var b = new RecordingModule("Foo", "Unique2");

            TsvrcGenerator.DetectAndExcludeFieldNameCollisions(new List<TsvrcModule> { a, b });

            CollectionAssert.Contains(a.ExcludedNames, "Foo");
            CollectionAssert.Contains(b.ExcludedNames, "Foo");
            CollectionAssert.DoesNotContain(a.ExcludedNames, "Unique1");
            CollectionAssert.DoesNotContain(b.ExcludedNames, "Unique2");
        }

        [Test]
        public void NoCollision_ExcludeFieldNamesNeverCalled()
        {
            var a = new RecordingModule("Alpha");
            var b = new RecordingModule("Beta");

            TsvrcGenerator.DetectAndExcludeFieldNameCollisions(new List<TsvrcModule> { a, b });

            Assert.IsEmpty(a.ExcludedNames);
            Assert.IsEmpty(b.ExcludedNames);
        }

        [Test]
        public void ThreeWayCollision_AllThreeModulesReceiveTheName()
        {
            var a = new RecordingModule("Shared");
            var b = new RecordingModule("Shared");
            var c = new RecordingModule("Shared");

            TsvrcGenerator.DetectAndExcludeFieldNameCollisions(new List<TsvrcModule> { a, b, c });

            Assert.IsTrue(a.ExcludedNames.Contains("Shared"));
            Assert.IsTrue(b.ExcludedNames.Contains("Shared"));
            Assert.IsTrue(c.ExcludedNames.Contains("Shared"));
        }

        [Test]
        public void ModuleThatDoesNotOverrideExposedFieldNames_NeverParticipates()
        {
            // The default TsvrcModule.ExposedFieldNames() is Enumerable.Empty<string>() -
            // a module that doesn't override it (like ConstructModule/FactoryModule/
            // PoolModule in production) can never collide with anything through this path.
            var defaultModule = new RecordingModuleWithDefaultExposedNames();
            var other = new RecordingModule("AnyName");

            Assert.DoesNotThrow(() =>
                TsvrcGenerator.DetectAndExcludeFieldNameCollisions(new List<TsvrcModule> { defaultModule, other }));
            Assert.IsEmpty(other.ExcludedNames);
        }

        private class RecordingModuleWithDefaultExposedNames : TsvrcModule
        {
            internal override void LoadConfig() { }
        }
    }
}
