using NUnit.Framework;
using System;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // GlobalModule.Wire() against a real compiled root in an isolated temp scene, fed
    // synthetic entries via reflection (bypassing LoadConfig()).
    //
    // This project ships with no real configured Global entries, so the mechanism here is
    // exercised against "_memory", a field that unconditionally exists on the compiled type
    // regardless of config (MemoryModule's own field). GlobalModule.Wire() only cares that
    // FindProperty(entry.Name) resolves and that the source object's type matches the field's
    // declared type. It has no idea which module "owns" the field name, so this is a
    // faithful, if borrowed, test of the exact same mechanical assignment path a real
    // Global field goes through.
    public class GlobalModuleWireTests
    {
        private static readonly Type EntryType = CodeGenModuleReflection.NestedType(typeof(GlobalModule), "GlobalEntry");
        private const string RealFieldName = "_memory";

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
            => CodeGenModuleReflection.BuildEntry(EntryType, ("Name", name), ("TypeName", ""), ("Namespace", ""), ("SourceObject", source));

        private static GlobalModule ModuleWith(params object[] entries)
        {
            var module = new GlobalModule();
            PrivateFieldAccess.SetField(module, "_entries", CodeGenModuleReflection.BuildList(EntryType, entries));
            return module;
        }

        private UnityEngine.Object FieldValue(string name)
            => new SerializedObject(_root).FindProperty(name).objectReferenceValue;

        [Test]
        public void Wire_FieldFound_DirectReferenceIsAssigned()
        {
            var memory = _scope.CreateGameObject("SomeMemory").AddComponent<TsvrcMemory>();
            var module = ModuleWith(Entry(RealFieldName, memory));

            module.Wire();

            Assert.AreEqual(memory, FieldValue(RealFieldName));
        }

        [Test]
        public void Wire_FieldNotFound_WarnsButDoesNotThrow()
        {
            var target = _scope.CreateGameObject("Target");
            var module = ModuleWith(Entry("DefinitelyNotARealField", target));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field 'DefinitelyNotARealField' not found.*"));

            Assert.DoesNotThrow(() => module.Wire());
        }

        [Test]
        public void Wire_OneEntryMissingFieldAmongMultiple_OtherEntryStillWired()
        {
            var memory = _scope.CreateGameObject("SomeMemory").AddComponent<TsvrcMemory>();
            var module = ModuleWith(
                Entry("DefinitelyNotARealField", memory),
                Entry(RealFieldName, memory));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field 'DefinitelyNotARealField' not found.*"));

            module.Wire();

            Assert.AreEqual(memory, FieldValue(RealFieldName), "The missing-field entry must not abort wiring of the remaining valid entries.");
        }

        [Test]
        public void Wire_RestoredEntryWithNullSourceObject_FieldBecomesNullWithoutThrowing()
        {
            // Mirrors exactly what ApplySnapshotFallback's fromSnapshot delegate produces for a
            // restored entry, see its own doc comment. GenerateCode() is protected across a
            // transient broken compile, but Wire() has no name-only way to recover a real scene
            // reference, so it (harmlessly) writes null into the field instead of throwing.
            var memory = _scope.CreateGameObject("SomeMemory").AddComponent<TsvrcMemory>();
            ModuleWith(Entry(RealFieldName, memory)).Wire();
            Assert.AreEqual(memory, FieldValue(RealFieldName), "Sanity check: the field really was wired before simulating the restore.");

            var module = ModuleWith(Entry(RealFieldName, null));

            Assert.DoesNotThrow(() => module.Wire());
            Assert.IsNull(FieldValue(RealFieldName));
        }

        [Test]
        public void Wire_RootAbsent_DoesNotThrow()
        {
            _scope.Dispose();
            _scope = new TempSceneScope(); // no compiled root added this time
            var module = ModuleWith(Entry("Anything", null));

            Assert.DoesNotThrow(() => module.Wire());
        }
    }
}
