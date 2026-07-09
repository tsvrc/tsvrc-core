using System;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // FactoryModule.OnSceneHierarchyChanged().
    public class FactoryModuleHierarchyChangedTests
    {
        private static readonly Type EntryType = CodeGenModuleReflection.NestedType(typeof(FactoryModule), "FactoryEntry");

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static FactoryModule ModuleWithEntryCount(int count)
        {
            var entries = new object[count];
            for (int i = 0; i < count; i++)
                entries[i] = CodeGenModuleReflection.BuildEntry(EntryType,
                    ("Name", "E" + i), ("TypeName", "GameObject"), ("TypeNamespace", ""),
                    ("IsTsvrcBehaviour", false), ("PrefabAsset", null));

            var module = new FactoryModule();
            PrivateFieldAccess.SetField(module, "_entries", CodeGenModuleReflection.BuildList(EntryType, entries));
            return module;
        }

        [Test]
        public void OnSceneHierarchyChanged_ZeroEntries_AlwaysReturnsFalseEvenWithStrayContainer()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var stray = _scope.CreateGameObject("Factories");
            stray.transform.SetParent(root.transform, false);

            Assert.IsFalse(ModuleWithEntryCount(0).OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_RootAbsent_ReturnsFalse()
        {
            Assert.IsFalse(ModuleWithEntryCount(1).OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_NonEmptyEntriesChildAbsent_ReturnsTrue()
        {
            CompiledRootFixture.AddTo(_scope);

            Assert.IsTrue(ModuleWithEntryCount(1).OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_NonEmptyEntriesChildPresent_ReturnsFalse()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var child = _scope.CreateGameObject("Factories");
            child.transform.SetParent(root.transform, false);

            Assert.IsFalse(ModuleWithEntryCount(1).OnSceneHierarchyChanged());
        }
    }
}
