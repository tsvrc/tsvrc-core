using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Utils;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // LogModule.Wire()/AfterFilesStable() against a real compiled root in an isolated
    // temp scene. Mirrors MemoryModuleWireTests.
    public class LogModuleWireTests
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

        private static UnityEngine.Object LogFieldValue(Component root)
            => new SerializedObject(root).FindProperty("_log").objectReferenceValue;

        [Test]
        public void Wire_TsLoggerPresentInScene_FieldAssigned()
        {
            var logGo = _scope.CreateGameObject("TsLogger");
            var log = logGo.AddComponent<TsLogger>();

            new LogModule().Wire();

            Assert.AreEqual(log, LogFieldValue(_root));
        }

        [Test]
        public void Wire_NoTsLoggerInScene_FieldLeftNull()
        {
            new LogModule().Wire();

            Assert.IsNull(LogFieldValue(_root));
        }

        [Test]
        public void Wire_CalledTwiceWithSameLogger_SecondCallLeavesFieldUnchanged()
        {
            var logGo = _scope.CreateGameObject("TsLogger");
            var log = logGo.AddComponent<TsLogger>();
            var module = new LogModule();
            module.Wire();

            module.Wire();

            Assert.AreEqual(log, LogFieldValue(_root));
        }

        [Test]
        public void AfterFilesStable_TsLoggerChildAbsent_IsCreated()
        {
            bool programAssetMissing = new LogModule().AfterFilesStable();

            Assert.IsNotNull(_root.transform.Find("TsLogger"));
            Assert.IsNotNull(_root.transform.Find("TsLogger").GetComponent<TsLogger>());
            Assert.IsFalse(programAssetMissing, "TsLogger.asset already exists in this project.");
        }

        [Test]
        public void AfterFilesStable_TsLoggerChildAlreadyPresent_LeftAlone()
        {
            var existing = _scope.CreateGameObject("TsLogger");
            existing.transform.SetParent(_root.transform, false);
            existing.AddComponent<TsLogger>();

            new LogModule().AfterFilesStable();

            Assert.AreEqual(1, _root.transform.childCount, "Must not create a second TsLogger child.");
        }
    }
}
