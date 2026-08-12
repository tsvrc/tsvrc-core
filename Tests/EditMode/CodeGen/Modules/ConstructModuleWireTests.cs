using NUnit.Framework;
using System;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // ConstructModule.Wire() against a real compiled root in an isolated temp scene.
    // Unlike Memory/Instance, Construct's fields are all `_construct{Name}` - there is no
    // fixed field name that exists on the compiled type without a prior real config-driven
    // generation pass (this project has none by default), so only the
    // missing-field/null-source/root-absent branches are testable here. Wire() and
    // GenerateCode() both call the same private FieldName() helper, so the name they agree on
    // can never drift, and the mechanical SerializedProperty assignment the "field found ->
    // assigned" path performs is the identical one proven end-to-end by
    // GlobalModuleWireTests.Wire_FieldFound_DirectReferenceIsAssigned.
    public class ConstructModuleWireTests
    {
        private static readonly Type EntryType = CodeGenModuleReflection.NestedType(typeof(TsModule), "ResolvedEntry");

        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            CompiledRootFixture.AddTo(_scope); // Wire() finds the compiled root itself via FindRoot()
        }

        [TearDown]
        public void TearDown() => _scope.Dispose();

        private static object Entry(string name, UnityEngine.Object source)
            => CodeGenModuleReflection.BuildEntry(EntryType, ("Name", name), ("TypeName", ""), ("Namespace", ""), ("SourceObject", source));

        private static ConstructModule ModuleWith(params object[] entries)
        {
            var module = new ConstructModule();
            PrivateFieldAccess.SetField(module, "_entries", CodeGenModuleReflection.BuildList(EntryType, entries));
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
