using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // SingletonModule.Resolve() called directly via reflection with fully synthetic
    // UnityEngine.Object instances - no AssetDatabase, no TsvrcConfig/TsvrcBuiltinConfig
    // asset touched at all (CODEGEN_TESTING_PLAN.md Part 2.2, Obstacle B / Phase G3.1).
    public class SingletonModuleResolveTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static IList Resolve(IEnumerable<Object> objects)
            => (IList)PrivateFieldAccess.InvokeStatic(typeof(SingletonModule), "Resolve", objects);

        private static string NameOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "Name");
        private static string TypeNameOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "TypeName");

        [Test]
        public void Resolve_NullEntry_WarnsAndSkips()
        {
            LogAssert.Expect(LogType.Warning, "[SingletonModule] Null entry in config, remove the missing-script slot.");

            var result = Resolve(new Object[] { null });

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Resolve_DuplicateObjectReference_KeepsFirstWarnsOnSecond()
        {
            var go = _scope.CreateGameObject("Dup");

            LogAssert.Expect(LogType.Warning, "[SingletonModule] Duplicate entry 'Dup' in config, remove the duplicate.");

            var result = Resolve(new Object[] { go, go });

            Assert.AreEqual(1, result.Count);
        }

        [Test]
        public void Resolve_PlainComponent_NameIsTypeName()
        {
            var go = _scope.CreateGameObject("AnyName");
            var transform = go.transform;

            var result = Resolve(new Object[] { transform });

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("Transform", NameOf(result[0]));
            Assert.AreEqual("Transform", TypeNameOf(result[0]));
        }

        [Test]
        public void Resolve_AliasedGameObjectName_AliasWinsOverTypeName()
        {
            var go = _scope.CreateGameObject("__MyAlias__");

            var result = Resolve(new Object[] { go.transform });

            Assert.AreEqual("MyAlias", NameOf(result[0]));
        }

        [Test]
        public void Resolve_AnimatorType_GetsAnimatorSuffixInsteadOfBareTypeName()
        {
            var go = _scope.CreateGameObject("Char");
            var animator = go.AddComponent<Animator>();

            var result = Resolve(new Object[] { animator });

            Assert.AreEqual("CharAnimator", NameOf(result[0]));
        }

        [Test]
        public void Resolve_AliasedAnimator_AliasPlusAnimatorSuffix()
        {
            var go = _scope.CreateGameObject("__Hero__");
            var animator = go.AddComponent<Animator>();

            var result = Resolve(new Object[] { animator });

            Assert.AreEqual("HeroAnimator", NameOf(result[0]));
        }

        [Test]
        public void Resolve_TwoDifferentObjectsWouldProduceSameName_SecondGetsDeduplicatedSuffix()
        {
            var goA = _scope.CreateGameObject("__Foo__");
            var goB = _scope.CreateGameObject("__Foo__");

            var result = Resolve(new Object[] { goA.transform, goB.transform });

            Assert.AreEqual("Foo", NameOf(result[0]));
            Assert.AreEqual("Foo2", NameOf(result[1]));
        }

        [Test]
        public void Resolve_CombinedSceneThenBuiltinStream_SceneEntryKeepsUnsuffixedName()
        {
            // Resolve() is fed scene-then-builtin concatenated (SingletonModule.LoadConfig
            // builds `sceneSingletons.Concat(builtinConfig.Singletons)`), so iteration order
            // alone determines which of two same-named entries wins the unsuffixed name -
            // the scene one, since it's always listed first in the concatenation.
            var sceneObj = _scope.CreateGameObject("__Foo__");
            var builtinObj = _scope.CreateGameObject("__Foo__"); // stands in for a builtin-sourced object

            var result = Resolve(new Object[] { sceneObj.transform, builtinObj.transform });

            Assert.AreEqual("Foo", NameOf(result[0]), "The first (scene-position) entry keeps the unsuffixed name.");
            Assert.AreEqual("Foo2", NameOf(result[1]));
        }
    }
}
