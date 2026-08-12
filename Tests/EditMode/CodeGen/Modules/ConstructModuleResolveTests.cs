using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // ConstructModule.Resolve() via reflection with real (but scene-only, throwaway)
    // components. No builtin source exists for Constructs - scene-only, unlike
    // Global/Pool/Factory. Takes IEnumerable<Object>, not TsvrcBehaviour[]: TsGroupedEntry.Value
    // is plain Object, so a currently-uncompiled entry reaches Resolve() instead of being
    // silently dropped by an early `as TsvrcBehaviour` cast (see TryResolveObjectType).
    public class ConstructModuleResolveTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        // These ungrouped tests pass an empty prefix and no explicit name; the prefixed path is
        // covered by Resolve_GroupPrefix_IsPrependedToTheName and the explicit-name path by
        // ResolveNamed.
        private static IList Resolve(Object[] constructs)
            => ResolveFull(constructs.Select(o => (o, string.Empty, string.Empty)));

        private static IList ResolveNamed(IEnumerable<(Object value, string name)> inputs)
            => ResolveFull(inputs.Select(t => (t.value, string.Empty, t.name)));

        private static IList ResolvePrefixed(IEnumerable<(Object value, string prefix)> inputs)
            => ResolveFull(inputs.Select(t => (t.value, t.prefix, string.Empty)));

        private static IList ResolveFull(IEnumerable<(Object value, string prefix, string name)> inputs)
            => (IList)PrivateFieldAccess.InvokeStatic(typeof(ConstructModule), "Resolve", (object)inputs.ToList());

        private static string NameOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "Name");
        private static string TypeNameOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "TypeName");

        [Test]
        public void Resolve_NullEntry_WarnsAndSkips()
        {
            LogAssert.Expect(LogType.Warning, "[ConstructModule] Null entry in Constructs config, remove the missing-script slot.");

            var result = Resolve(new Object[] { null });

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Resolve_DuplicateBehaviourReference_KeepsFirstWarnsOnSecond()
        {
            var behaviour = _scope.CreateGameObject("Hud").AddComponent<StateManager>();

            LogAssert.Expect(LogType.Warning, "[ConstructModule] Duplicate construct 'Hud' in config, remove the duplicate.");

            var result = Resolve(new Object[] { behaviour, behaviour });

            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void Resolve_PlainName_UsesTypeNameWhenNoAlias()
        {
            var behaviour = _scope.CreateGameObject("AnyName").AddComponent<StateManager>();

            var result = Resolve(new Object[] { behaviour });

            Assert.AreEqual("StateManager", NameOf(result[0]));
        }

        [Test]
        public void Resolve_ExplicitName_WinsOverTypeName()
        {
            var behaviour = _scope.CreateGameObject("AnyName").AddComponent<StateManager>();

            var result = ResolveNamed(new[] { ((Object)behaviour, "Hud") });

            Assert.AreEqual("Hud", NameOf(result[0]));
        }

        [Test]
        public void Resolve_TwoExplicitNamesCollide_SecondGetsDeduplicatedSuffix()
        {
            var a = _scope.CreateGameObject("A").AddComponent<StateManager>();
            var b = _scope.CreateGameObject("B").AddComponent<StateManager>();

            var result = ResolveNamed(new[] { ((Object)a, "Dup"), ((Object)b, "Dup") });

            Assert.AreEqual("Dup", NameOf(result[0]));
            Assert.AreEqual("Dup2", NameOf(result[1]));
        }

        [Test]
        public void Resolve_GroupPrefix_IsPrependedToTheName()
        {
            var behaviour = _scope.CreateGameObject("AnyName").AddComponent<StateManager>();

            var result = ResolvePrefixed(new[] { ((Object)behaviour, "Hud") });

            Assert.AreEqual("HudStateManager", NameOf(result[0]));
        }

        [Test]
        public void Resolve_NonComponentObject_WarnsAndSkips()
        {
            // TsGroupedEntry.Value is plain Object, so a non-component reference
            // must be rejected explicitly rather than crashing on an invalid cast downstream.
            var asset = ScriptableObject.CreateInstance<TestScriptableObject>();
            try
            {
                LogAssert.Expect(LogType.Warning, $"[ConstructModule] '{asset.name}' is not a component. It must be a TsvrcBehaviour on a scene object. Skipping.");

                var result = Resolve(new Object[] { asset });

                Assert.AreEqual(0, result.Count);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Resolve_ComponentNotATsvrcBehaviour_WarnsAndSkips()
        {
            // Transform always resolves a real type via reflection, but must still be rejected
            // for not being a TsvrcBehaviour, exercising the IsTsvrcBehaviourType gate that runs
            // after type resolution now succeeds.
            var transform = _scope.CreateGameObject("NotABehaviour").transform;

            LogAssert.Expect(LogType.Warning, "[ConstructModule] 'NotABehaviour' (Transform) is not a TsvrcBehaviour. Skipping.");

            var result = Resolve(new Object[] { transform });

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Resolve_RealTsvrcBehaviour_ResolvesTypeAndNamespaceViaTryResolveObjectType()
        {
            var behaviour = _scope.CreateGameObject("AnyName").AddComponent<StateManager>();

            var result = Resolve(new Object[] { behaviour });

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("StateManager", TypeNameOf(result[0]));
        }

        private class TestScriptableObject : ScriptableObject { }
    }
}
