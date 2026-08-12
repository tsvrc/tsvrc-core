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

        // These ungrouped tests pass an empty prefix and no explicit name; the prefixed path is
        // covered by Resolve_GroupPrefix_* and the explicit-name path by ResolveNamed below.
        private static IList Resolve(IEnumerable<Object> objects)
            => ResolveFull(objects.Select(o => (o, string.Empty, string.Empty)));

        private static IList ResolveNamed(IEnumerable<(Object value, string name)> inputs)
            => ResolveFull(inputs.Select(t => (t.value, string.Empty, t.name)));

        private static IList ResolvePrefixed(IEnumerable<(Object value, string prefix)> inputs)
            => ResolveFull(inputs.Select(t => (t.value, t.prefix, string.Empty)));

        private static IList ResolveFull(IEnumerable<(Object value, string prefix, string name)> inputs)
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
        public void Resolve_ExplicitName_WinsOverGameObjectOwnName()
        {
            var go = _scope.CreateGameObject("Villain");

            var result = ResolveNamed(new[] { ((Object)go, "Hero") });

            Assert.AreEqual("Hero", NameOf(result[0]));
        }

        [Test]
        public void Resolve_ExplicitName_WinsOverComponentTypeName()
        {
            var go = _scope.CreateGameObject("AnyName");

            var result = ResolveNamed(new[] { ((Object)go.transform, "MyAlias") });

            Assert.AreEqual("MyAlias", NameOf(result[0]));
        }

        [Test]
        public void Resolve_ExplicitName_SanitizedToValidIdentifier()
        {
            var go = _scope.CreateGameObject("AnyName");

            var result = ResolveNamed(new[] { ((Object)go.transform, "my hud 1!") });

            Assert.AreEqual("MyHud1", NameOf(result[0]));
        }

        [Test]
        public void Resolve_BlankExplicitName_FallsBackToDerivedDefault()
        {
            var go = _scope.CreateGameObject("AnyName");

            var result = ResolveNamed(new[] { ((Object)go.transform, "   ") });

            Assert.AreEqual("Transform", NameOf(result[0]));
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
        public void Resolve_ExplicitNameOnAnimator_UsedVerbatimWithoutAnimatorSuffix()
        {
            // The +Animator suffix is part of the derived default; an explicit name replaces it.
            var go = _scope.CreateGameObject("Char");
            var animator = go.AddComponent<Animator>();

            var result = ResolveNamed(new[] { ((Object)animator, "Rig") });

            Assert.AreEqual("Rig", NameOf(result[0]));
        }

        [Test]
        public void Resolve_TwoExplicitNamesCollide_SecondGetsDeduplicatedSuffix()
        {
            var a = _scope.CreateGameObject("A");
            var b = _scope.CreateGameObject("B");

            var result = ResolveNamed(new[] { ((Object)a.transform, "Foo"), ((Object)b.transform, "Foo") });

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
            var a = _scope.CreateGameObject("Spawner");
            var b = _scope.CreateGameObject("Spawner");

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
            // Resolve() is fed scene entries before builtin ones, so iteration order alone decides
            // which of two same-named entries keeps the unsuffixed name: the scene one, listed first.
            var sceneObj = _scope.CreateGameObject("Scene");
            var builtinObj = _scope.CreateGameObject("Builtin"); // stands in for a builtin-sourced object

            var result = ResolveNamed(new[] { ((Object)sceneObj.transform, "Foo"), ((Object)builtinObj.transform, "Foo") });

            Assert.AreEqual("Foo", NameOf(result[0]), "The first (scene-position) entry keeps the unsuffixed name.");
            Assert.AreEqual("Foo2", NameOf(result[1]));
        }
    }
}
