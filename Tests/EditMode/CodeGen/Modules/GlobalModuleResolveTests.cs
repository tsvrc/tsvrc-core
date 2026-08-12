using NUnit.Framework;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // GlobalModule.Resolve() called directly via reflection with fully synthetic
    // UnityEngine.Object instances - no AssetDatabase, no TsConfig/TsBuiltinConfig
    // asset touched at all.
    public class GlobalModuleResolveTests
    {
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp() => _scope = new TempSceneScope();

        [TearDown]
        public void TearDown() => _scope.Dispose();

        // These ungrouped tests pass an empty prefix; the prefixed path is covered by
        // Resolve_GroupPrefix_* below.
        private static IList Resolve(IEnumerable<Object> objects)
            => ResolvePrefixed(objects.Select(o => (o, string.Empty)));

        private static IList ResolvePrefixed(IEnumerable<(Object value, string prefix)> inputs)
            => (IList)PrivateFieldAccess.InvokeStatic(typeof(GlobalModule), "Resolve", (object)inputs.ToList());

        private static string NameOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "Name");
        private static string TypeNameOf(object entry) => PrivateFieldAccess.GetField<string>(entry, "TypeName");

        [Test]
        public void Resolve_NullEntry_WarnsAndSkips()
        {
            LogAssert.Expect(LogType.Warning, "[GlobalModule] Null entry in config, remove the missing-script slot.");

            var result = Resolve(new Object[] { null });

            Assert.AreEqual(0, result.Count);
        }

        [Test]
        public void Resolve_DuplicateObjectReference_KeepsFirstWarnsOnSecond()
        {
            var go = _scope.CreateGameObject("Dup");

            LogAssert.Expect(LogType.Warning, "[GlobalModule] Duplicate entry 'Dup' in config, remove the duplicate.");

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
        public void Resolve_PlainGameObject_NameIsGameObjectOwnNameNotTheTypeName()
        {
            // A plain GameObject resolves to typeName "GameObject"; it must be named after the
            // object, not that literal type name.
            var go = _scope.CreateGameObject("MyThing");

            var result = Resolve(new Object[] { go });

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("MyThing", NameOf(result[0]));
            Assert.AreEqual("GameObject", TypeNameOf(result[0]));
        }

        [Test]
        public void Resolve_PlainGameObjectNameWithSeparators_SanitizedToValidIdentifier()
        {
            var go = _scope.CreateGameObject("My Thing 1");

            var result = Resolve(new Object[] { go });

            Assert.AreEqual("MyThing1", NameOf(result[0]));
        }

        [Test]
        public void Resolve_TwoPlainGameObjects_GetDistinctNamesFromTheirOwnNames()
        {
            var alpha = _scope.CreateGameObject("Alpha");
            var beta = _scope.CreateGameObject("Beta");

            var result = Resolve(new Object[] { alpha, beta });

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("Alpha", NameOf(result[0]));
            Assert.AreEqual("Beta", NameOf(result[1]));
        }

        [Test]
        public void Resolve_PlainGameObjectNameSanitizesToNothing_FallsBackToTypeName()
        {
            var go = _scope.CreateGameObject("***");

            var result = Resolve(new Object[] { go });

            Assert.AreEqual("GameObject", NameOf(result[0]));
        }

        [Test]
        public void Resolve_AliasedPlainGameObject_AliasWinsOverGameObjectOwnName()
        {
            var go = _scope.CreateGameObject("__Hero__");

            var result = Resolve(new Object[] { go });

            Assert.AreEqual("Hero", NameOf(result[0]));
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
        public void Resolve_GroupPrefix_IsPrependedToTheDerivedName()
        {
            var go = _scope.CreateGameObject("AnyName");
            var transform = go.transform;

            var result = ResolvePrefixed(new[] { ((Object)transform, "EnemiesBoss") });

            Assert.AreEqual("EnemiesBossTransform", NameOf(result[0]));
        }

        [Test]
        public void Resolve_GroupPrefix_DisambiguatesOtherwiseCollidingEntries()
        {
            var a = _scope.CreateGameObject("__Spawner__");
            var b = _scope.CreateGameObject("__Spawner__");

            var result = ResolvePrefixed(new[]
            {
                ((Object)a, "Enemies"),
                ((Object)b, "Allies"),
            });

            Assert.AreEqual("EnemiesSpawner", NameOf(result[0]));
            Assert.AreEqual("AlliesSpawner", NameOf(result[1]), "Distinct prefixes avoid the numeric dedupe suffix.");
        }

        [Test]
        public void Resolve_CombinedSceneThenBuiltinStream_SceneEntryKeepsUnsuffixedName()
        {
            // Resolve() is fed scene-then-builtin concatenated (GlobalModule.LoadConfig
            // builds `sceneGlobals.Concat(builtinGlobals)`), so iteration order
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
