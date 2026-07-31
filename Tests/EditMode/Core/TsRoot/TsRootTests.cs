using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.Utils;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsRoot is abstract with exactly two independent virtual properties
    // (Instance, Memory), both defaulting to null. Tests here pin the default-null
    // value, that the two properties' overrides are independent of each other, and
    // the abstract/UdonSharpBehaviour shape the rest of the library
    // (TsvrcBehaviour._ts, ScaffoldModule.FindCompiledType()) depends on.
    public class TsRootTests
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
            var root = Create<TestTsRoot>();

            Assert.IsNull(root.Instance);
        }

        [Test]
        public void Memory_NoOverride_DefaultsToNull()
        {
            var root = Create<TestTsRoot>();

            Assert.IsNull(root.Memory);
        }

        [Test]
        public void Instance_OverriddenAlone_MemoryStaysNull()
        {
            var root = Create<InstanceOnlyTsRootDouble>();
            root.FakeInstance = Create<Instance>();

            Assert.AreSame(root.FakeInstance, root.Instance);
            Assert.IsNull(root.Memory);
        }

        [Test]
        public void Memory_OverriddenAlone_InstanceStaysNull()
        {
            var root = Create<MemoryOnlyTsRootDouble>();
            root.FakeMemory = Create<TsvrcMemory>();

            Assert.AreSame(root.FakeMemory, root.Memory);
            Assert.IsNull(root.Instance);
        }

        [Test]
        public void Instance_And_Memory_BothOverridden_ReturnIndependentValues()
        {
            var root = Create<FullTsRootDouble>();
            root.FakeInstance = Create<Instance>();
            root.FakeMemory = Create<TsvrcMemory>();

            Assert.AreSame(root.FakeInstance, root.Instance);
            Assert.AreSame(root.FakeMemory, root.Memory);
            Assert.AreNotSame(root.Instance, (object)root.Memory);
        }

        [Test]
        public void TsRoot_IsAbstract()
        {
            Assert.IsTrue(typeof(TsRoot).IsAbstract,
                "TsRoot must stay abstract - every TsvrcBehaviour reaches it only through " +
                "_ts, never through the generated TsGenerated type directly, per the class's " +
                "own header comment.");
        }

        [Test]
        public void TsRoot_IsUdonSharpBehaviour()
        {
            Assert.IsTrue(typeof(UdonSharp.UdonSharpBehaviour).IsAssignableFrom(typeof(TsRoot)));
        }

        [Test]
        public void ConcreteDouble_UsableAsTsConstructArgument_EndToEnd()
        {
            var root = Create<FullTsRootDouble>();
            root.FakeInstance = Create<Instance>();
            var behaviour = Create<TsvrcBehaviourTestSubclass>();

            behaviour.TsConstruct((TsRoot)root);

            Assert.AreSame(root, behaviour.GetTs());
            Assert.AreSame(root.FakeInstance, behaviour.GetTs().Instance);
        }
    }
}
