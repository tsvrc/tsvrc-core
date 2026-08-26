using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // Tests TsGenerator.DetectAndExcludeFieldNameCollisions() in isolation from Run(). Real
    // modules that override ExposedFieldNames() each dedupe their own entries first, so the
    // cross-module path is mostly exercised through synthetic modules like RecordingModule below,
    // except where a test targets a real module's own ExposedFieldNames()/FieldNamePrecedence.
    public class TsGeneratorFieldCollisionTests
    {
        private class RecordingModule : TsModule
        {
            private readonly string[] _exposedNames;
            private readonly int _precedence;
            internal List<string> ExcludedNames { get; } = new List<string>();

            internal RecordingModule(params string[] exposedNames) : this(0, exposedNames) { }
            internal RecordingModule(int precedence, params string[] exposedNames)
            {
                _precedence = precedence;
                _exposedNames = exposedNames;
            }

            internal override void LoadConfig() { }
            internal override IEnumerable<string> ExposedFieldNames() => _exposedNames;
            internal override void ExcludeFieldNames(IEnumerable<string> names) => ExcludedNames.AddRange(names);
            internal override int FieldNamePrecedence => _precedence;
        }

        [Test]
        public void TwoModulesExposeSameName_BothReceiveItViaExcludeFieldNames()
        {
            var a = new RecordingModule("Foo", "Unique1");
            var b = new RecordingModule("Foo", "Unique2");

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { a, b });

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

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { a, b });

            Assert.IsEmpty(a.ExcludedNames);
            Assert.IsEmpty(b.ExcludedNames);
        }

        [Test]
        public void HigherPrecedenceModuleKeepsName_LowerPrecedenceModuleDropsIt()
        {
            // The Construct-supersedes-Global case: a construct (high precedence) exposing the same
            // name as a global (default precedence) keeps it; the global drops it, and it is NOT
            // reported as a genuine collision.
            var global = new RecordingModule(0, "GameManager");
            var construct = new RecordingModule(100, "GameManager");

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { global, construct });

            CollectionAssert.Contains(global.ExcludedNames, "GameManager");
            CollectionAssert.DoesNotContain(construct.ExcludedNames, "GameManager");
            CollectionAssert.DoesNotContain(TsGenerator.LastFieldNameCollisions, "GameManager");
        }

        [Test]
        public void EqualHighPrecedenceModules_StillGenuineCollision_BothDrop()
        {
            var a = new RecordingModule(100, "Shared");
            var b = new RecordingModule(100, "Shared");

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { a, b });

            CollectionAssert.Contains(a.ExcludedNames, "Shared");
            CollectionAssert.Contains(b.ExcludedNames, "Shared");
            CollectionAssert.Contains(TsGenerator.LastFieldNameCollisions, "Shared");
        }

        [Test]
        public void ThreeWayCollision_AllThreeModulesReceiveTheName()
        {
            var a = new RecordingModule("Shared");
            var b = new RecordingModule("Shared");
            var c = new RecordingModule("Shared");

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { a, b, c });

            Assert.IsTrue(a.ExcludedNames.Contains("Shared"));
            Assert.IsTrue(b.ExcludedNames.Contains("Shared"));
            Assert.IsTrue(c.ExcludedNames.Contains("Shared"));
        }

        [Test]
        public void TwoModulesExposeSameName_LastFieldNameCollisionsRecordsIt()
        {
            var a = new RecordingModule("Foo");
            var b = new RecordingModule("Foo");

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { a, b });

            CollectionAssert.Contains(TsGenerator.LastFieldNameCollisions, "Foo");
        }

        [Test]
        public void NoCollision_LastFieldNameCollisionsIsEmpty()
        {
            var a = new RecordingModule("Alpha");
            var b = new RecordingModule("Beta");

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { a, b });

            Assert.IsEmpty(TsGenerator.LastFieldNameCollisions);
        }

        [Test]
        public void PreviousCollisionResolved_LastFieldNameCollisionsClearsOnNextCall()
        {
            // A collision the user fixed (e.g. by setting a distinct Name) must not linger in the
            // recorded list forever - each call reflects only its own pass.
            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule>
            {
                new RecordingModule("Foo"), new RecordingModule("Foo"),
            });
            Assert.IsNotEmpty(TsGenerator.LastFieldNameCollisions);

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule>
            {
                new RecordingModule("Alpha"), new RecordingModule("Beta"),
            });

            Assert.IsEmpty(TsGenerator.LastFieldNameCollisions);
        }

        [Test]
        public void ModuleThatDoesNotOverrideExposedFieldNames_NeverParticipates()
        {
            // The default TsModule.ExposedFieldNames() is Enumerable.Empty<string>() -
            // a module that doesn't override it (like FactoryModule/PoolModule in production,
            // whose generated members are methods, not fields) can never collide with anything
            // through this path.
            var defaultModule = new RecordingModuleWithDefaultExposedNames();
            var other = new RecordingModule("AnyName");

            Assert.DoesNotThrow(() =>
                TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { defaultModule, other }));
            Assert.IsEmpty(other.ExcludedNames);
        }

        private class RecordingModuleWithDefaultExposedNames : TsModule
        {
            internal override void LoadConfig() { }
        }

        // "Instance"/"Log"/"Memory" are unconditional, reserved identifiers, always present in
        // generated code. Guards against a Global entry auto-deriving the same name (e.g. a
        // GameObject literally named "Instance") producing a silent duplicate-member compile
        // error instead of a normal, reported collision.
        [Test]
        public void InstanceModule_ExposesInstance_AtReservedPrecedence()
        {
            var instanceModule = new InstanceModule();

            CollectionAssert.AreEqual(new[] { "Instance" }, instanceModule.ExposedFieldNames());
            Assert.AreEqual(TsModule.ReservedFieldNamePrecedence, instanceModule.FieldNamePrecedence);
        }

        [Test]
        public void LogModule_ExposesLog_AtReservedPrecedence()
        {
            var logModule = new LogModule();

            CollectionAssert.AreEqual(new[] { "Log" }, logModule.ExposedFieldNames());
            Assert.AreEqual(TsModule.ReservedFieldNamePrecedence, logModule.FieldNamePrecedence);
        }

        [Test]
        public void MemoryModule_ExposesMemory_AtReservedPrecedence()
        {
            var memoryModule = new MemoryModule();

            CollectionAssert.AreEqual(new[] { "Memory" }, memoryModule.ExposedFieldNames());
            Assert.AreEqual(TsModule.ReservedFieldNamePrecedence, memoryModule.FieldNamePrecedence);
        }

        [Test]
        public void GlobalEntryNamedInstance_CollidesWithInstanceModule_GlobalLoses()
        {
            var instanceModule = new InstanceModule();
            var globalLikeModule = new RecordingModule(0, "Instance");

            TsGenerator.DetectAndExcludeFieldNameCollisions(new List<TsModule> { instanceModule, globalLikeModule });

            CollectionAssert.Contains(globalLikeModule.ExcludedNames, "Instance");
            CollectionAssert.DoesNotContain(TsGenerator.LastFieldNameCollisions, "Instance");
        }
    }
}
