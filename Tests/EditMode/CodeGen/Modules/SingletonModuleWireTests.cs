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
    // SingletonModule.Wire() against a real compiled root in an isolated temp scene, fed
    // synthetic entries via reflection (bypassing LoadConfig()).
    //
    // This project ships with no real configured Singleton entries, so the mechanism here is
    // exercised against "_memory" - a field that unconditionally exists on the compiled type
    // regardless of config (MemoryModule's own field). SingletonModule.Wire() only cares that
    // FindProperty(entry.Name) resolves and that the source object's type matches the field's
    // declared type - it has no idea which module "owns" the field name, so this is a
    // faithful, if borrowed, test of the exact same mechanical assignment path a real
    // Singleton field goes through.
    public class SingletonModuleWireTests
    {
        private static readonly Type EntryType = CodeGenModuleReflection.NestedType(typeof(SingletonModule), "SingletonEntry");
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

        private static SingletonModule ModuleWith(params object[] entries)
        {
            var module = new SingletonModule();
            PrivateFieldAccess.SetField(module, "_entries", CodeGenModuleReflection.BuildList(EntryType, entries));
            return module;
        }

        private UnityEngine.Object FieldValue(string name)
            => new SerializedObject(_root).FindProperty(name).objectReferenceValue;

        [Test]
        public void Wire_FieldFound_DirectReferenceIsAssigned()
        {
            var memory = _scope.CreateGameObject("SomeMemory").AddComponent<TsMemory>();
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
            var memory = _scope.CreateGameObject("SomeMemory").AddComponent<TsMemory>();
            var module = ModuleWith(
                Entry("DefinitelyNotARealField", memory),
                Entry(RealFieldName, memory));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*Field 'DefinitelyNotARealField' not found.*"));

            module.Wire();

            Assert.AreEqual(memory, FieldValue(RealFieldName), "The missing-field entry must not abort wiring of the remaining valid entries.");
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
