using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.Utils;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Validates the field-hiding pattern ScaffoldModule.GenerateCode() emits for every
    // generated shadow class (see ScaffoldModuleGenerateCodeTests): a same-named property
    // that hides the inherited TsRoot-typed _ts field with one retyped to the concrete
    // generated root. This is the mechanism the refactor depends on for world scripts (e.g.
    // MolInstance : TsInstance) to reach project-specific global/pool/construct/
    // factory members through plain `_ts.Member` calls, with no cast at any call site.
    //
    // Exercised here against ExtensionPointBehaviourDouble/ExtensionPointStateManagerDouble
    // (see TsExtensionPointShadowDoubles.cs) instead of the real generated TsGenerated type,
    // which only exists in a consuming project's Assembly-CSharp and can never be referenced
    // from Tsvrc's own test assembly.
    public class TsExtensionPointShadowPatternTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private T Create<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void ShadowedTs_AfterTsConstruct_IsTypedAsTheConcreteRoot_NotTsRoot()
        {
            var root = Create<FullTsRootDouble>();
            var behaviour = Create<ExtensionPointBehaviourDouble>();

            behaviour.TsConstruct((TsRoot)root);

            Assert.AreSame(root, behaviour.GetShadowedTs());
        }

        [Test]
        public void ShadowedTs_AndBaseTs_ReferenceTheSameUnderlyingInstance()
        {
            // The property must never diverge from the field it hides. It's a cast view onto
            // the exact same TsConstruct-assigned reference, not an independent value.
            var root = Create<FullTsRootDouble>();
            var behaviour = Create<ExtensionPointBehaviourDouble>();

            behaviour.TsConstruct((TsRoot)root);

            Assert.AreSame(behaviour.GetBaseTs(), behaviour.GetShadowedTs());
        }

        [Test]
        public void ShadowedTs_NeverConstructed_IsNull()
        {
            var behaviour = Create<ExtensionPointBehaviourDouble>();

            Assert.IsNull(behaviour.GetShadowedTs());
        }

        [Test]
        public void ShadowedTs_ExposesConcreteMembersNotOnTsRoot_WithNoCastAtCallSite()
        {
            // The actual bug being fixed: a member declared only on the concrete root
            // (FakeInstance stands in for a project's generated global/pool/factory member)
            // must be reachable through _ts directly, unqualified, from a leaf world script.
            var root = Create<FullTsRootDouble>();
            var instance = Create<Instance>();
            root.FakeInstance = instance;
            var leaf = Create<ExtensionPointBehaviourLeafDouble>();

            leaf.TsConstruct((TsRoot)root);

            Assert.AreSame(instance, leaf.ReadInstanceThroughShadow());
        }

        [Test]
        public void ShadowPattern_SurvivesPropagationThroughTsConstructParent()
        {
            var root = Create<FullTsRootDouble>();
            var parent = Create<ExtensionPointBehaviourDouble>();
            parent.TsConstruct((TsRoot)root);
            var child = Create<ExtensionPointBehaviourLeafDouble>();

            child.TsConstruct(parent);

            Assert.AreSame(root, child.GetShadowedTs());
        }

        [Test]
        public void ShadowPattern_AlsoWorksWhenInsertedBelowAFrameworkExtensionPointClass()
        {
            // Mirrors TsStateManager : StateManager (and TsInstance : Instance,
            // TsListItem : ListItem, TsProcess : Process): the shadow property still
            // resolves correctly with a Tsvrc framework class between TsvrcBehaviour and
            // the world's own leaf script.
            var root = Create<FullTsRootDouble>();
            root.FakeMemory = Create<TsvrcMemory>();
            var leaf = Create<ExtensionPointStateManagerLeafDouble>();

            leaf.TsConstruct((TsRoot)root);

            Assert.AreSame(root.FakeMemory, leaf.ReadMemoryThroughShadow());
        }
    }
}
