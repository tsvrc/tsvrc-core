using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.Utils;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // TsvrcRoot is abstract with exactly two independent virtual properties
    // (Instance, Memory), both defaulting to null. Tests here pin the default-null
    // value, that the two properties' overrides are independent of each other, and
    // the abstract/UdonSharpBehaviour shape the rest of the library
    // (TsvrcBehaviour._ts, ScaffoldModule.FindCompiledType()) depends on.
    public class TsvrcRootTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        private T Create<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void Instance_NoOverride_DefaultsToNull()
        {
            var root = Create<TestTsvrcRoot>();

            Assert.IsNull(root.Instance);
        }

        [Test]
        public void Memory_NoOverride_DefaultsToNull()
        {
            var root = Create<TestTsvrcRoot>();

            Assert.IsNull(root.Memory);
        }

        [Test]
        public void Instance_OverriddenAlone_MemoryStaysNull()
        {
            var root = Create<InstanceOnlyTsvrcRootDouble>();
            root.FakeInstance = Create<TsvrcInstance>();

            Assert.AreSame(root.FakeInstance, root.Instance);
            Assert.IsNull(root.Memory);
        }

        [Test]
        public void Memory_OverriddenAlone_InstanceStaysNull()
        {
            var root = Create<MemoryOnlyTsvrcRootDouble>();
            root.FakeMemory = Create<TsMemory>();

            Assert.AreSame(root.FakeMemory, root.Memory);
            Assert.IsNull(root.Instance);
        }

        [Test]
        public void Instance_And_Memory_BothOverridden_ReturnIndependentValues()
        {
            var root = Create<FullTsvrcRootDouble>();
            root.FakeInstance = Create<TsvrcInstance>();
            root.FakeMemory = Create<TsMemory>();

            Assert.AreSame(root.FakeInstance, root.Instance);
            Assert.AreSame(root.FakeMemory, root.Memory);
            Assert.AreNotSame(root.Instance, (object)root.Memory);
        }

        [Test]
        public void TsvrcRoot_IsAbstract()
        {
            Assert.IsTrue(typeof(TsvrcRoot).IsAbstract,
                "TsvrcRoot must stay abstract - every TsvrcBehaviour reaches it only through " +
                "_ts, never through the generated TsvrcGenerated type directly, per the class's " +
                "own header comment.");
        }

        [Test]
        public void TsvrcRoot_IsUdonSharpBehaviour()
        {
            Assert.IsTrue(typeof(UdonSharp.UdonSharpBehaviour).IsAssignableFrom(typeof(TsvrcRoot)));
        }

        [Test]
        public void ConcreteDouble_UsableAsTsConstructArgument_EndToEnd()
        {
            var root = Create<FullTsvrcRootDouble>();
            root.FakeInstance = Create<TsvrcInstance>();
            var behaviour = Create<TsvrcBehaviourTestSubclass>();

            behaviour.TsConstruct((TsvrcRoot)root);

            Assert.AreSame(root, behaviour.GetTs());
            Assert.AreSame(root.FakeInstance, behaviour.GetTs().Instance);
        }
    }
}
