using System;
using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.Editor
{
    // ConstructModule.Wire() against a real compiled root in an isolated temp scene.
    // Phase G4.4. Unlike Memory/Instance, Construct's fields are all `_construct{Name}` -
    // there is no fixed field name that exists on the compiled type without a prior real
    // config-driven generation pass (this project has none by default - see
    // CODEGEN_TESTING_PLAN.md Part 4.5), so the missing-field/null-source/root-absent
    // branches are always testable, while Wire_RealConstructField_... additionally runs the
    // real "field found -> assigned" path whenever CodeGenSandbox.Bootstrap() has been
    // applied (see run-codegen-sandbox-tests.ps1), and is Assert.Ignore()'d otherwise.
    public class ConstructModuleWireTests
    {
        private static readonly Type EntryType = PrivateFieldAccess.NestedType(typeof(ConstructModule), "ConstructEntry");

        private TempSceneScope _scope;
        private Component _root;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _root = CompiledRootFixture.AddTo(_scope);
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

        [Test]
        public void Wire_RealConstructField_DirectReferenceIsAssigned()
        {
            // Runs for real once CodeGenSandbox.Bootstrap() has produced a real
            // "_constructSampleConstruct" field on TsvrcGenerated; Assert.Ignore()s
            // otherwise. See run-codegen-sandbox-tests.ps1.
            SandboxGate.RequireField(_root, CodeGenSandbox.ConstructFieldName);

            var behaviour = _scope.CreateGameObject("SomeConstructTarget").AddComponent<StateManager>();
            var module = ModuleWith(Entry(CodeGenSandbox.ConstructEntryName, behaviour));

            module.Wire();

            var value = new SerializedObject(_root).FindProperty(CodeGenSandbox.ConstructFieldName).objectReferenceValue;
            Assert.AreEqual(behaviour, value);
        }
    }
}
