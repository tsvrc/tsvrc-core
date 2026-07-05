using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // MemoryModule.Wire()/AfterFilesStable() against a real compiled root in an isolated
    // temp scene. Phase G4.1/G4.2.
    public class MemoryModuleWireTests
    {
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

        private static UnityEngine.Object MemoryFieldValue(Component root)
            => new SerializedObject(root).FindProperty("_memory").objectReferenceValue;

        [Test]
        public void Wire_TsMemoryPresentInScene_FieldAssigned()
        {
            var memoryGo = _scope.CreateGameObject("TsvrcMemory");
            var memory = memoryGo.AddComponent<TsMemory>();

            new MemoryModule().Wire();

            Assert.AreEqual(memory, MemoryFieldValue(_root));
        }

        [Test]
        public void Wire_NoTsMemoryInScene_FieldLeftNull()
        {
            new MemoryModule().Wire();

            Assert.IsNull(MemoryFieldValue(_root));
        }

        [Test]
        public void Wire_CalledTwiceWithSameMemory_SecondCallLeavesFieldUnchanged()
        {
            var memoryGo = _scope.CreateGameObject("TsvrcMemory");
            var memory = memoryGo.AddComponent<TsMemory>();
            var module = new MemoryModule();
            module.Wire();

            module.Wire();

            // The prop.objectReferenceValue == memory short-circuit means this second call
            // never touches ApplyModifiedProperties - the field simply still holds the same
            // reference, which is the externally-observable half of that guarantee.
            Assert.AreEqual(memory, MemoryFieldValue(_root));
        }

        [Test]
        public void AfterFilesStable_TsvrcMemoryChildAbsent_IsCreated()
        {
            bool programAssetMissing = new MemoryModule().AfterFilesStable();

            Assert.IsNotNull(_root.transform.Find("TsvrcMemory"));
            Assert.IsNotNull(_root.transform.Find("TsvrcMemory").GetComponent<TsMemory>());
            Assert.IsFalse(programAssetMissing, "TsMemory.asset already exists in this project.");
        }

        [Test]
        public void AfterFilesStable_TsvrcMemoryChildAlreadyPresent_LeftAlone()
        {
            var existing = _scope.CreateGameObject("TsvrcMemory");
            existing.transform.SetParent(_root.transform, false);
            existing.AddComponent<TsMemory>();

            new MemoryModule().AfterFilesStable();

            Assert.AreEqual(1, _root.transform.childCount, "Must not create a second TsvrcMemory child.");
        }
    }
}
