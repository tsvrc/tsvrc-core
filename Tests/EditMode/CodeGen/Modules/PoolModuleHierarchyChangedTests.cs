using NUnit.Framework;
using System.Collections.Generic;
using System.Collections;
using System;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // PoolModule.OnSceneHierarchyChanged(). Note: this only checks child *count*, not
    // per-slot identity - a same-count-but-corrupted-slot scenario would NOT be caught here
    // (it relies on IsPoolAlreadyWired inside the next Wire() call instead).
    public class PoolModuleHierarchyChangedTests
    {
        private static readonly Type InfoType = CodeGenModuleReflection.NestedType(typeof(PoolModule), "PoolTypeInfo");

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static PoolModule ModuleWithExpectedTotal(int poolEntryCount, int expectedTotalSlots)
        {
            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), InfoType);
            var dict = (IDictionary)Activator.CreateInstance(dictType, StringComparer.Ordinal);
            if (expectedTotalSlots > 0)
            {
                var info = CodeGenModuleReflection.BuildEntry(InfoType,
                    ("Prefab", null), ("TypeName", "X"), ("TypeNamespace", ""),
                    ("ExternalCount", 0), ("InternalDeps", new Dictionary<string, int>(StringComparer.Ordinal)),
                    ("TotalSlots", expectedTotalSlots));
                dict["X"] = info;
            }

            var module = new PoolModule();
            var entries = new List<(UnityEngine.Component, string)>();
            for (int i = 0; i < poolEntryCount; i++) entries.Add((null, "X"));
            PrivateFieldAccess.SetField(module, "_poolEntries", entries);
            PrivateFieldAccess.SetField(module, "_poolTypeInfos", dict);
            return module;
        }

        [Test]
        public void OnSceneHierarchyChanged_RootAbsent_ReturnsFalse()
        {
            Assert.IsFalse(ModuleWithExpectedTotal(0, 0).OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_ZeroPoolEntries_ReflectsStrayContainerPresence()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            Assert.IsFalse(ModuleWithExpectedTotal(0, 0).OnSceneHierarchyChanged(), "No stray container, zero entries -> false.");

            var stray = _scope.CreateGameObject("Pool");
            stray.transform.SetParent(root.transform, false);
            Assert.IsTrue(ModuleWithExpectedTotal(0, 0).OnSceneHierarchyChanged(), "Stray container present with zero entries -> true.");
        }

        [Test]
        public void OnSceneHierarchyChanged_NonEmptyEntriesContainerAbsent_ReturnsTrue()
        {
            CompiledRootFixture.AddTo(_scope);

            Assert.IsTrue(ModuleWithExpectedTotal(1, 2).OnSceneHierarchyChanged());
        }

        [Test]
        public void OnSceneHierarchyChanged_ContainerChildCountMismatch_ReturnsTrue()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var container = _scope.CreateGameObject("Pool");
            container.transform.SetParent(root.transform, false);
            var onlyChild = _scope.CreateGameObject("X_0");
            onlyChild.transform.SetParent(container.transform, false);

            Assert.IsTrue(ModuleWithExpectedTotal(1, 2).OnSceneHierarchyChanged(), "Expects 2 children but only 1 exists.");
        }

        [Test]
        public void OnSceneHierarchyChanged_ContainerChildCountMatches_ReturnsFalse()
        {
            var root = CompiledRootFixture.AddTo(_scope);
            var container = _scope.CreateGameObject("Pool");
            container.transform.SetParent(root.transform, false);
            for (int i = 0; i < 2; i++)
            {
                var child = _scope.CreateGameObject($"X_{i}");
                child.transform.SetParent(container.transform, false);
            }

            Assert.IsFalse(ModuleWithExpectedTotal(1, 2).OnSceneHierarchyChanged());
        }
    }
}
