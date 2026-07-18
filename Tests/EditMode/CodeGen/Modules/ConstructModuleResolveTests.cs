using System.Collections;
using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // ConstructModule.Resolve() via reflection with real (but scene-only, throwaway)
    // TsBehaviour components. No builtin source exists for Constructs - scene-only,
    // unlike Singleton/Pool/Factory.
    public class ConstructModuleResolveTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static IList Resolve(TsBehaviour[] constructs)
            => (IList)PrivateFieldAccess.InvokeStatic(typeof(ConstructModule), "Resolve", (object)constructs);

        private static string NameOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "Name");

        [Test]
        public void Resolve_NullConstructsArray_ReturnsEmptyList()
        {
            Assert.AreEqual(0, Resolve(null).Count);
        }

        [Test]
        public void Resolve_NullEntry_WarnsAndSkips()
        {
            LogAssert.Expect(LogType.Warning, "[ConstructModule] Null entry in Constructs config, remove the missing-script slot.");

            var result = Resolve(new TsBehaviour[] { null });

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Resolve_DuplicateBehaviourReference_KeepsFirstWarnsOnSecond()
        {
            var behaviour = _scope.CreateGameObject("Hud").AddComponent<StateManager>();

            LogAssert.Expect(LogType.Warning, "[ConstructModule] Duplicate construct 'Hud' in config, remove the duplicate.");

            var result = Resolve(new TsBehaviour[] { behaviour, behaviour });

            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void Resolve_PlainName_UsesTypeNameWhenNoAlias()
        {
            var behaviour = _scope.CreateGameObject("AnyName").AddComponent<StateManager>();

            var result = Resolve(new TsBehaviour[] { behaviour });

            Assert.AreEqual("StateManager", NameOf(result[0]));
        }

        [Test]
        public void Resolve_AliasedName_AliasWinsOverTypeName()
        {
            var behaviour = _scope.CreateGameObject("__Hud__").AddComponent<StateManager>();

            var result = Resolve(new TsBehaviour[] { behaviour });

            Assert.AreEqual("Hud", NameOf(result[0]));
        }

        [Test]
        public void Resolve_TwoEntriesCollideOnName_SecondGetsDeduplicatedSuffix()
        {
            var a = _scope.CreateGameObject("__Dup__").AddComponent<StateManager>();
            var b = _scope.CreateGameObject("__Dup__").AddComponent<StateManager>();

            var result = Resolve(new TsBehaviour[] { a, b });

            Assert.AreEqual("Dup", NameOf(result[0]));
            Assert.AreEqual("Dup2", NameOf(result[1]));
        }
    }
}
