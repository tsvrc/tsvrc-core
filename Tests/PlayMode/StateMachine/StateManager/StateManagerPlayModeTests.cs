using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.PlayMode.StateMachine.StateManager
{
    // StateManager never touches Networking/VRCPlayerApi, so no ClientSim is needed here.
    // This covers only real deferred GameObject destruction around Dispatch's destroyed-target
    // skip check: DestroyImmediate is synchronous, but a real Destroy() call defers actual
    // destruction to end of frame.
    public class StateManagerPlayModeTests
    {
        private GameObject _managerGameObject;
        private GameObject _targetGameObject;

        [TearDown]
        public void TearDown()
        {
            if (_managerGameObject != null) Object.DestroyImmediate(_managerGameObject);
            if (_targetGameObject != null) Object.DestroyImmediate(_targetGameObject);
        }

        [UnityTest]
        public IEnumerator SetState_TargetDestroyedByEndOfFrame_SkipsDispatchSilently()
        {
            _managerGameObject = new GameObject(nameof(StateManagerPlayModeTests));
            var manager = _managerGameObject.AddComponent<Tsvrc.StateMachine.StateManager>();
            _targetGameObject = new GameObject("Target");
            var target = _targetGameObject.AddComponent<StateTargetDouble>();
            manager.RegisterState(1, nameof(StateTargetDouble.EnterA), null, target);

            Object.Destroy(_targetGameObject);
            yield return null;
            Assert.IsTrue(_targetGameObject == null,
                "Setup sanity check: the target must actually be destroyed by end of frame.");

            Assert.DoesNotThrow(() => manager.SetState(1));
            Assert.AreEqual(0, target.EnterACount,
                "Dispatch must skip silently once the real target GameObject is destroyed.");
        }

        [UnityTest]
        public IEnumerator SetState_TargetDestroyedThenTransitionedToInSameFrame_DispatchesBeforeRealDestructionCompletes()
        {
            _managerGameObject = new GameObject(nameof(StateManagerPlayModeTests));
            var manager = _managerGameObject.AddComponent<Tsvrc.StateMachine.StateManager>();
            _targetGameObject = new GameObject("Target");
            var target = _targetGameObject.AddComponent<StateTargetDouble>();
            manager.RegisterState(1, nameof(StateTargetDouble.EnterA), null, target);

            Object.Destroy(_targetGameObject);
            // No yield here: SetState runs in the same frame as Destroy(), before Unity has
            // actually torn down the native object. Unlike DestroyImmediate (what Edit Mode uses),
            // Object.Destroy's overridden == does not report the object as gone until the real,
            // deferred destruction actually runs, so Dispatch still executes normally here.
            Assert.DoesNotThrow(() => manager.SetState(1));
            Assert.AreEqual(1, target.EnterACount,
                "A Destroy()-ed target is not yet actually gone in the same frame the Destroy() call " +
                "was made, so Dispatch still executes normally until the real destruction completes.");

            yield return null;
        }
    }
}
