using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine;

namespace Tsvrc.Tests.EditMode.Testing.Framework
{
    // TsCallbackRecorder itself, in isolation - TsvrcBehaviourTests already proves it works
    // correctly as a TsSubscribe/TsEmit listener; these cases prove the double's own counting
    // and logging contract directly, independent of the pub/sub machinery it's normally driven
    // through.
    public class TsCallbackRecorderTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private TsCallbackRecorder Create(string name = "Recorder")
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<TsCallbackRecorder>();
        }

        [Test]
        public void CallbackA_Called_IncrementsCallbackACountOnly()
        {
            var recorder = Create();

            recorder.CallbackA();

            Assert.AreEqual(1, recorder.CallbackACount);
            Assert.AreEqual(0, recorder.CallbackBCount);
        }

        [Test]
        public void CallbackB_Called_IncrementsCallbackBCountOnly()
        {
            var recorder = Create();

            recorder.CallbackB();

            Assert.AreEqual(0, recorder.CallbackACount);
            Assert.AreEqual(1, recorder.CallbackBCount);
        }

        [Test]
        public void CallbackA_CalledMultipleTimes_CountAccumulates()
        {
            var recorder = Create();

            recorder.CallbackA();
            recorder.CallbackA();
            recorder.CallbackA();

            Assert.AreEqual(3, recorder.CallbackACount);
        }

        [Test]
        public void Callbacks_NullLog_DoesNotThrow()
        {
            var recorder = Create();

            Assert.DoesNotThrow(() => recorder.CallbackA());
            Assert.DoesNotThrow(() => recorder.CallbackB());
        }

        [Test]
        public void Callbacks_WithLog_AppendsGameObjectNameQualifiedEntry()
        {
            var recorder = Create("MyRecorder");
            var log = new List<string>();
            recorder.Log = log;

            recorder.CallbackA();
            recorder.CallbackB();

            CollectionAssert.AreEqual(new[] { "MyRecorder.CallbackA", "MyRecorder.CallbackB" }, log);
        }

        [Test]
        public void Callbacks_SharedLogAcrossInstances_PreservesCallOrder()
        {
            var log = new List<string>();
            var first = Create("First");
            var second = Create("Second");
            first.Log = log;
            second.Log = log;

            first.CallbackA();
            second.CallbackA();
            first.CallbackB();

            CollectionAssert.AreEqual(new[] { "First.CallbackA", "Second.CallbackA", "First.CallbackB" }, log);
        }
    }
}
