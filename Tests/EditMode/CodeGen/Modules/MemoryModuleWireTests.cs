using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // MemoryModule.Wire()/AfterFilesStable() against a real compiled root in an isolated
    // temp scene.
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
            var memoryGo = _scope.CreateGameObject("TsMemory");
            var memory = memoryGo.AddComponent<TsvrcMemory>();

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
            var memoryGo = _scope.CreateGameObject("TsMemory");
            var memory = memoryGo.AddComponent<TsvrcMemory>();
            var module = new MemoryModule();
            module.Wire();

            module.Wire();

            // The prop.objectReferenceValue == memory short-circuit means this second call
            // never touches ApplyModifiedProperties - the field simply still holds the same
            // reference, which is the externally-observable half of that guarantee.
            Assert.AreEqual(memory, MemoryFieldValue(_root));
        }

        [Test]
        public void AfterFilesStable_TsMemoryChildAbsent_IsCreated()
        {
            bool programAssetMissing = new MemoryModule().AfterFilesStable();

            Assert.IsNotNull(_root.transform.Find("TsMemory"));
            Assert.IsNotNull(_root.transform.Find("TsMemory").GetComponent<TsvrcMemory>());
            Assert.IsFalse(programAssetMissing, "TsvrcMemory.asset already exists in this project.");
        }

        [Test]
        public void AfterFilesStable_TsMemoryChildAlreadyPresent_LeftAlone()
        {
            var existing = _scope.CreateGameObject("TsMemory");
            existing.transform.SetParent(_root.transform, false);
            existing.AddComponent<TsvrcMemory>();

            new MemoryModule().AfterFilesStable();

            Assert.AreEqual(1, _root.transform.childCount, "Must not create a second TsMemory child.");
        }
    }
}
