using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.UdonTestKit;
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

        [TearDown]
        public void TearDown()
        {
            if (_managerGameObject != null) Object.DestroyImmediate(_managerGameObject);
        }

        [UnityTest]
        public IEnumerator SetState_TargetDestroyedByEndOfFrame_SkipsDispatchSilently()
        {
            using var calls = new CallLog();
            _managerGameObject = new GameObject(nameof(StateManagerPlayModeTests));
            var manager = _managerGameObject.AddComponent<Tsvrc.StateMachine.StateManager>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(1, target, nameof(StateTargetSpy.EnterA), null);

            Object.Destroy(target.gameObject);
            yield return null;
            Assert.IsTrue(target == null,
                "Setup sanity check: the target must actually be destroyed by end of frame.");

            Assert.DoesNotThrow(() => manager.SetState(1));
            CollectionAssert.IsEmpty(calls,
                "Dispatch must skip silently once the real target GameObject is destroyed.");
        }

        [UnityTest]
        public IEnumerator SetState_TargetDestroyedThenTransitionedToInSameFrame_DispatchesBeforeRealDestructionCompletes()
        {
            using var calls = new CallLog();
            _managerGameObject = new GameObject(nameof(StateManagerPlayModeTests));
            var manager = _managerGameObject.AddComponent<Tsvrc.StateMachine.StateManager>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(1, target, nameof(StateTargetSpy.EnterA), null);

            Object.Destroy(target.gameObject);
            // No yield here: SetState runs in the same frame as Destroy(), before Unity has
            // actually torn down the native object. Unlike DestroyImmediate (what Edit Mode uses),
            // Object.Destroy's overridden == does not report the object as gone until the real,
            // deferred destruction actually runs, so Dispatch still executes normally here.
            Assert.DoesNotThrow(() => manager.SetState(1));
            CollectionAssert.AreEqual(new[] { "Target.EnterA" }, calls,
                "A Destroy()-ed target is not yet actually gone in the same frame the Destroy() call " +
                "was made, so Dispatch still executes normally until the real destruction completes.");

            yield return null;
        }
    }
}
