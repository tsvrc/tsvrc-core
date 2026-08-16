using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine;

namespace Tsvrc.Tests.EditMode.Testing.Framework
{
    // TsRootBuilder composes a project's generated composition root from code instead of
    // loading a saved scene - see its own doc comment. Deliberately exercised here against
    // plain, made-up root/child types rather than a real TsGenerated: TsRootBuilder itself has
    // no dependency on Tsvrc.Runtime (the six bootstrap methods are invoked by name via
    // reflection), so its own tests shouldn't need a real generated project either.
    public class TsRootBuilderTests
    {
        private class ChildComponent : MonoBehaviour
        {
        }

        private abstract class AbstractChildComponent : MonoBehaviour
        {
        }

        private class FullRoot : MonoBehaviour
        {
            public ChildComponent PublicComponentField;
            private ChildComponent _privateComponentField;
            public GameObject PublicGameObjectField;
            public string PublicStringField;
            private AbstractChildComponent _abstractField;

            public ChildComponent PrivateComponentFieldValue => _privateComponentField;
            public AbstractChildComponent AbstractFieldValue => _abstractField;

            public readonly List<string> CallLog = new List<string>();
            public void _TsLogStart() => CallLog.Add(nameof(_TsLogStart));
            public void _TsMemoryStart() => CallLog.Add(nameof(_TsMemoryStart));
            public void _TsGlobalStart() => CallLog.Add(nameof(_TsGlobalStart));
            public void _TsPoolStart() => CallLog.Add(nameof(_TsPoolStart));
            public void _TsConstructStart() => CallLog.Add(nameof(_TsConstructStart));
            public void _TsInstanceStart() => CallLog.Add(nameof(_TsInstanceStart));
        }

        private class PartialRoot : MonoBehaviour
        {
            public int GlobalStartCallCount;
            public void _TsGlobalStart() => GlobalStartCallCount++;
        }

        [Test]
        public void Constructor_CreatesRootImmediately_BeforeBuildRuns()
        {
            var builder = new TsRootBuilder<FullRoot>();

            Assert.IsNotNull(builder.Root);
            Assert.AreEqual(nameof(FullRoot), builder.Root.gameObject.name);

            builder.Teardown();
        }

        [Test]
        public void With_AssignsGivenInstanceToNamedField()
        {
            var builder = new TsRootBuilder<FullRoot>();
            var go = new GameObject("ExplicitChild");
            var child = go.AddComponent<ChildComponent>();

            builder.With("PublicComponentField", child);

            Assert.AreSame(child, builder.Root.PublicComponentField);
            builder.Teardown();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void With_ReachesPrivateFieldsToo()
        {
            var builder = new TsRootBuilder<FullRoot>();
            var go = new GameObject("ExplicitPrivateChild");
            var child = go.AddComponent<ChildComponent>();

            builder.With("_privateComponentField", child);

            Assert.AreSame(child, builder.Root.PrivateComponentFieldValue);
            builder.Teardown();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void WithNew_CreatesInstanceAndAssignsItToField_AndReturnsIt()
        {
            var builder = new TsRootBuilder<FullRoot>();

            var created = builder.WithNew<ChildComponent>("PublicComponentField");

            Assert.IsNotNull(created);
            Assert.AreSame(created, builder.Root.PublicComponentField);
            builder.Teardown();
        }

        [Test]
        public void Build_InvokesEveryPresentBootstrapMethod_InGeneratorOrder()
        {
            var builder = new TsRootBuilder<FullRoot>();

            builder.Build();

            CollectionAssert.AreEqual(
                new[] { "_TsLogStart", "_TsMemoryStart", "_TsGlobalStart", "_TsPoolStart", "_TsConstructStart", "_TsInstanceStart" },
                builder.Root.CallLog);
            builder.Teardown();
        }

        [Test]
        public void Build_RootWithOnlySomeBootstrapMethods_InvokesOnlyThoseWithoutThrowing()
        {
            var builder = new TsRootBuilder<PartialRoot>();

            Assert.DoesNotThrow(() => builder.Build());

            Assert.AreEqual(1, builder.Root.GlobalStartCallCount);
            builder.Teardown();
        }

        [Test]
        public void Build_AutoFillsUnsetPublicComponentField()
        {
            var builder = new TsRootBuilder<FullRoot>();

            builder.Build();

            Assert.IsNotNull(builder.Root.PublicComponentField);
            builder.Teardown();
        }

        [Test]
        public void Build_AutoFillsUnsetPrivateComponentField()
        {
            var builder = new TsRootBuilder<FullRoot>();

            builder.Build();

            Assert.IsNotNull(builder.Root.PrivateComponentFieldValue);
            builder.Teardown();
        }

        [Test]
        public void Build_DoesNotOverwriteAnAlreadySetComponentField()
        {
            var builder = new TsRootBuilder<FullRoot>();
            var explicitChild = builder.WithNew<ChildComponent>("PublicComponentField");

            builder.Build();

            Assert.AreSame(explicitChild, builder.Root.PublicComponentField);
            builder.Teardown();
        }

        [Test]
        public void Build_SkipsAbstractComponentTypedField_LeavesItNull()
        {
            var builder = new TsRootBuilder<FullRoot>();

            Assert.DoesNotThrow(() => builder.Build());

            Assert.IsNull(builder.Root.AbstractFieldValue);
            builder.Teardown();
        }

        [Test]
        public void Build_DoesNotTouchNonComponentFields()
        {
            var builder = new TsRootBuilder<FullRoot>();

            builder.Build();

            Assert.IsNull(builder.Root.PublicGameObjectField);
            Assert.IsNull(builder.Root.PublicStringField);
            builder.Teardown();
        }

        [Test]
        public void Teardown_DestroysRootAndEveryCreatedStandIn()
        {
            var builder = new TsRootBuilder<FullRoot>();
            var root = builder.Root;
            var explicitChild = builder.WithNew<ChildComponent>("PublicComponentField");
            builder.Build(); // auto-fills _privateComponentField too

            builder.Teardown();

            Assert.IsTrue(root == null, "Root GameObject must be destroyed.");
            Assert.IsTrue(explicitChild == null, "WithNew-created GameObject must be destroyed.");
        }

        [Test]
        public void Teardown_CalledTwice_DoesNotThrow()
        {
            var builder = new TsRootBuilder<FullRoot>();
            builder.Build();
            builder.Teardown();

            Assert.DoesNotThrow(() => builder.Teardown());
        }
    }
}
