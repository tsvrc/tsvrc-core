using System;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // ConstructModule.Wire() against a real compiled root in an isolated temp scene.
    // Phase G4.4. Unlike Memory/Instance, Construct's fields are all `_construct{Name}` -
    // there is no fixed field name that exists on the compiled type without a prior real
    // config-driven generation pass (this project has none yet - see
    // SingletonModuleWireTests' constraint note for why), so only the missing-field/
    // null-source/root-absent branches are testable against the real compiled root here.
    public class ConstructModuleWireTests
    {
        private static readonly Type EntryType = PrivateFieldAccess.NestedType(typeof(ConstructModule), "ConstructEntry");

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            CompiledRootFixture.AddTo(_scope);
        }

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static object Entry(string name, UnityEngine.Object source)
            => PrivateFieldAccess.BuildEntry(EntryType, ("Name", name), ("TypeName", ""), ("Namespace", ""), ("SourceObject", source));

        private static ConstructModule ModuleWith(params object[] entries)
        {
            var module = new ConstructModule();
            PrivateFieldAccess.SetField(module, "_entries", PrivateFieldAccess.BuildList(EntryType, entries));
            return module;
        }

        [Test]
        public void Wire_FieldNotFound_WarnsButDoesNotThrow()
        {
            var target = _scope.CreateGameObject("Target");
            var module = ModuleWith(Entry("NoSuchConstruct", target));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field '_constructNoSuchConstruct' not found.*"));

            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void Wire_NullSourceObject_WarnsAndSkipsWithoutThrowing()
        {
            var module = ModuleWith(Entry("Anything", null));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Construct 'Anything' source object is null.*"));

            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void Wire_RootAbsent_DoesNotThrow()
        {
            _scope.Dispose();
            _scope = new TempSceneScope();
            var module = ModuleWith(Entry("Anything", null));

            Assert.DoesNotThrow(() => module.Wire());
        }
    }
}
