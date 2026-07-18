using System.Collections;
using Tsvrc.Tests.EditMode;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.PlayMode
{
    // StateManager touches no Networking/VRCPlayerApi API, so - same reasoning as
    // TsBehaviourTests - ClientSim adds nothing here and none of these tests use it.
    // Edit Mode (Tests/Editor/StateMachine/StateManager/StateManagerTests.cs) covers the
    // full logical surface. What Play Mode adds that Edit Mode structurally cannot:
    // real *deferred* UnityEngine.Object.Destroy() completing across an actual frame
    // boundary (Edit Mode can only assert Destroy() was invoked - see
    // TsBehaviourTests.TsDestroy_Default_DoesNotDestroySynchronously - Play Mode can
    // observe the GameObject actually gone afterward), and driving a transition chain
    // across real yielded frames rather than synchronously in one call stack.
    //
    // Per this project's established Play Mode methodology (TsTimerPlayModeTests,
    // TsInstancePlayModeTests): NUnit Assert failures inside a [UnityTest] are silent
    // in this environment, so every test here does its own manual pass/fail check and
    // logs exactly one PLAYMODE_TEST_RESULT marker rather than relying on Assert for the
    // actual assertion.
    public class StateManagerPlayModeTests
    {
        private const int StateA = 1;
        private const int StateB = 2;

        private static void LogResult(string testName, bool passed, string details)
        {
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);
        }

        [UnityTest]
        public IEnumerator SetState_ExitTargetRealDestroyCompletesAcrossFrame_NextTransitionSkipsItWithoutThrowing()
        {
            const string testName = "SetState_ExitTargetRealDestroyCompletesAcrossFrame_NextTransitionSkipsItWithoutThrowing";

            var managerGo = new GameObject("Manager");
            var manager = managerGo.AddComponent<StateManagerTestSubclass>();
            var targetGo = new GameObject("ExitTarget");
            var target = targetGo.AddComponent<StateTargetDouble>();
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetDouble.ExitA), target: target);
            manager.SetState(StateA);

            // Real, deferred Destroy() - unlike Edit Mode (where Destroy() outside Play
            // Mode is rejected synchronously and logged as an error), this actually
            // removes the component's GameObject at end of frame.
            Object.Destroy(targetGo);
            yield return null;
            bool targetActuallyDestroyed = targetGo == null;

            bool threw = false;
            try
            {
                manager.SetState(StateB);
            }
            catch
            {
                threw = true;
            }

            bool reachedStateB = manager.CurrentState == StateB;

            Object.Destroy(managerGo);
            yield return null;

            bool passed = targetActuallyDestroyed && !threw && reachedStateB;
            LogResult(testName, passed,
                "targetActuallyDestroyed=" + targetActuallyDestroyed + " threw=" + threw + " reachedStateB=" + reachedStateB);
        }

        [UnityTest]
        public IEnumerator SetState_EnterTargetRealDestroyCompletesAcrossFrame_NextTransitionSkipsItWithoutThrowing()
        {
            const string testName = "SetState_EnterTargetRealDestroyCompletesAcrossFrame_NextTransitionSkipsItWithoutThrowing";

            var managerGo = new GameObject("Manager");
            var manager = managerGo.AddComponent<StateManagerTestSubclass>();
            var targetGo = new GameObject("EnterTarget");
            var target = targetGo.AddComponent<StateTargetDouble>();
            manager.RegisterState(StateB, enterMethod: nameof(StateTargetDouble.EnterB), target: target);

            Object.Destroy(targetGo);
            yield return null;
            bool targetActuallyDestroyed = targetGo == null;

            bool threw = false;
            try
            {
                manager.SetState(StateB);
            }
            catch
            {
                threw = true;
            }

            bool reachedStateB = manager.CurrentState == StateB;
            bool enterNeverRan = target == null || target.EnterBCount == 0;

            Object.Destroy(managerGo);
            yield return null;

            bool passed = targetActuallyDestroyed && !threw && reachedStateB && enterNeverRan;
            LogResult(testName, passed,
                "targetActuallyDestroyed=" + targetActuallyDestroyed + " threw=" + threw +
                " reachedStateB=" + reachedStateB + " enterNeverRan=" + enterNeverRan);
        }

        [UnityTest]
        public IEnumerator SetState_TransitionChainDrivenAcrossRealFrames_FinalStateAndPreviousStateAreConsistent()
        {
            const string testName = "SetState_TransitionChainDrivenAcrossRealFrames_FinalStateAndPreviousStateAreConsistent";
            const int stateC = 3;

            var managerGo = new GameObject("Manager");
            var manager = managerGo.AddComponent<StateManagerTestSubclass>();
            var target = managerGo.AddComponent<StateTargetDouble>();
            manager.RegisterState(StateA, nameof(StateTargetDouble.EnterA), nameof(StateTargetDouble.ExitA), target);
            manager.RegisterState(StateB, nameof(StateTargetDouble.EnterB), nameof(StateTargetDouble.ExitB), target);
            manager.RegisterState(stateC, nameof(StateTargetDouble.EnterC), nameof(StateTargetDouble.ExitC), target);

            manager.SetState(StateA);
            yield return null;
            manager.SetState(StateB);
            yield return null;
            manager.SetState(stateC);
            yield return null;

            bool currentIsC = manager.CurrentState == stateC;
            bool previousIsB = manager.PreviousState == StateB;
            bool enteredEachOnce = target.EnterACount == 1 && target.EnterBCount == 1 && target.EnterCCount == 1;
            bool exitedEachOnceExceptFinal = target.ExitACount == 1 && target.ExitBCount == 1 && target.ExitCCount == 0;

            Object.Destroy(managerGo);
            yield return null;

            bool passed = currentIsC && previousIsB && enteredEachOnce && exitedEachOnceExceptFinal;
            LogResult(testName, passed,
                "currentIsC=" + currentIsC + " previousIsB=" + previousIsB +
                " enteredEachOnce=" + enteredEachOnce + " exitedEachOnceExceptFinal=" + exitedEachOnceExceptFinal);
        }

        [UnityTest]
        public IEnumerator SetState_ReentrantFromOnStateChanged_RealFrameRun_QueuedTransitionStillAppliesCorrectly()
        {
            const string testName = "SetState_ReentrantFromOnStateChanged_RealFrameRun_QueuedTransitionStillAppliesCorrectly";
            const int stateC = 3;

            var managerGo = new GameObject("Manager");
            var manager = managerGo.AddComponent<StateManagerReentrantSubclass>();
            var target = managerGo.AddComponent<StateTargetDouble>();
            manager.RegisterState(StateA, nameof(StateTargetDouble.EnterA), nameof(StateTargetDouble.ExitA), target);
            manager.RegisterState(StateB, nameof(StateTargetDouble.EnterB), nameof(StateTargetDouble.ExitB), target);
            manager.RegisterState(stateC, nameof(StateTargetDouble.EnterC), nameof(StateTargetDouble.ExitC), target);
            manager.ArmRedirect = true;
            manager.RedirectFrom = StateB;
            manager.RedirectTo = stateC;

            manager.SetState(StateA);
            yield return null;
            manager.SetState(StateB);
            yield return null;

            bool endedOnC = manager.CurrentState == stateC;
            bool previousIsB = manager.PreviousState == StateB;
            bool enterBFiredExactlyOnce = target.EnterBCount == 1;
            bool enterCFiredExactlyOnce = target.EnterCCount == 1;

            Object.Destroy(managerGo);
            yield return null;

            bool passed = endedOnC && previousIsB && enterBFiredExactlyOnce && enterCFiredExactlyOnce;
            LogResult(testName, passed,
                "endedOnC=" + endedOnC + " previousIsB=" + previousIsB +
                " enterBFiredExactlyOnce=" + enterBFiredExactlyOnce + " enterCFiredExactlyOnce=" + enterCFiredExactlyOnce);
        }
    }
}
