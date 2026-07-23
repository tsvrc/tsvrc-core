using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.PlayMode.Core.TsBehaviour
{
    // TsBehaviour never touches Networking/VRCPlayerApi (see
    // Tests/EditMode/Core/TsBehaviour/TsBehaviourTests.cs, which covers everything else
    // about this class). This is its only PlayMode test: Destroy(gameObject) defers to
    // end-of-frame in Play Mode, which EditMode cannot observe - Unity rejects the call
    // there outright instead.
    public class TsBehaviourPlayModeTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
                Object.DestroyImmediate(_gameObject);
        }

        [UnityTest]
        public IEnumerator TsDestroy_Default_ActuallyDestroysGameObjectByEndOfFrame()
        {
            var behaviour = new GameObject(nameof(TsBehaviourPlayModeTests))
                .AddComponent<TsBehaviourTestSubclass>();
            _gameObject = behaviour.gameObject;

            behaviour.TsDestroy();
            yield return null;

            Assert.IsTrue(_gameObject == null,
                "TsDestroy's default implementation calls Destroy(gameObject), which Unity " +
                "defers to end-of-frame in Play Mode - the object must be gone by the next frame.");
            _gameObject = null;
        }
    }
}
