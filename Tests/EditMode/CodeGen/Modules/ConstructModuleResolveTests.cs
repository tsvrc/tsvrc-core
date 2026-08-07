using NUnit.Framework;
using System.Collections;
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

        private static IList Resolve(Object[] constructs)
            => (IList)PrivateFieldAccess.InvokeStatic(typeof(ConstructModule), "Resolve", (object)constructs);

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
        public void Resolve_AliasedName_AliasWinsOverTypeName()
        {
            var behaviour = _scope.CreateGameObject("__Hud__").AddComponent<StateManager>();

            var result = Resolve(new Object[] { behaviour });

            Assert.AreEqual("Hud", NameOf(result[0]));
        }

        [Test]
        public void Resolve_TwoEntriesCollideOnName_SecondGetsDeduplicatedSuffix()
        {
            var a = _scope.CreateGameObject("__Dup__").AddComponent<StateManager>();
            var b = _scope.CreateGameObject("__Dup__").AddComponent<StateManager>();

            var result = Resolve(new Object[] { a, b });

            Assert.AreEqual("Dup", NameOf(result[0]));
            Assert.AreEqual("Dup2", NameOf(result[1]));
        }

        [Test]
        public void Resolve_NonComponentObject_WarnsAndSkips()
        {
            // TsGroupedEntry.Value is plain Object, so a non-component reference
            // must be rejected explicitly rather than crashing on an invalid cast downstream.
            var asset = ScriptableObject.CreateInstance<TestScriptableObject>();
            try
            {
                LogAssert.Expect(LogType.Warning, $"[ConstructModule] '{asset.name}' is not a component. Constructs must be TsvrcBehaviours on a scene object.");

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
