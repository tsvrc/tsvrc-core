using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.StateMachine;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // StateManager never touches Networking/VRCPlayerApi, so - same reasoning as
    // TsvrcBehaviourTests - nothing here needs ClientSim or Play Mode. Play Mode
    // coverage (Tests/PlayMode/StateMachine/StateManager/) is limited to the one thing
    // Edit Mode structurally can't prove: real deferred GameObject destruction and
    // multi-frame sequencing.
    public class StateManagerTests
    {
        private const int StateA = 1;
        private const int StateB = 2;
        private const int StateC = 3;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        private T CreateBehaviour<T>(string name = null) where T : UdonSharp.UdonSharpBehaviour
        {
            var go = new GameObject(name ?? typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void CurrentState_Initial_IsNegativeOne()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();

            Assert.AreEqual(-1, manager.CurrentState);
        }

        [Test]
        public void PreviousState_Initial_IsNegativeOne()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();

            Assert.AreEqual(-1, manager.PreviousState);
        }

        [Test]
        public void GetCurrentState_MatchesCurrentStateProperty()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            manager.SetState(StateA);

            Assert.AreEqual(manager.CurrentState, manager.GetCurrentState());
        }

        [Test]
        public void GetPreviousState_MatchesPreviousStateProperty()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            manager.SetState(StateA);
            manager.SetState(StateB);

            Assert.AreEqual(manager.PreviousState, manager.GetPreviousState());
        }

        [Test]
        public void RegisterState_DefaultTarget_DispatchesOnManagerItself()
        {
            var manager = CreateBehaviour<StateManagerSelfDispatchSubclass>();
            manager.RegisterState(StateA, enterMethod: nameof(StateManagerSelfDispatchSubclass.EnterSelf));

            manager.SetState(StateA);

            Assert.AreEqual(1, manager.EnterSelfCount);
        }

        [Test]
        public void RegisterState_ExplicitTarget_DispatchesOnThatTargetNotTheManager()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetDouble.EnterA), target: target);

            manager.SetState(StateA);

            Assert.AreEqual(1, target.EnterACount);
        }

        [Test]
        public void RegisterState_NullEnterAndExitMethods_TreatedAsEmptyAndNeverDispatched()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(StateA, target: target);
            manager.RegisterState(StateB, target: target);

            manager.SetState(StateA);
            manager.SetState(StateB);

            Assert.AreEqual(0, target.EnterACount);
            Assert.AreEqual(0, target.ExitACount);
        }

        [Test]
        public void RegisterState_ReRegisterSameState_OverwritesPreviousEntryEntirely()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var oldTarget = CreateBehaviour<StateTargetDouble>("Old");
            var newTarget = CreateBehaviour<StateTargetDouble>("New");
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetDouble.EnterA), target: oldTarget);

            manager.RegisterState(StateA, enterMethod: nameof(StateTargetDouble.EnterB), target: newTarget);
            manager.SetState(StateA);

            Assert.AreEqual(0, oldTarget.EnterACount, "Old registration must be fully replaced, not merged.");
            Assert.AreEqual(1, newTarget.EnterBCount);
        }

        [Test]
        public void UnregisterState_RemovesEntry_SubsequentTransitionSkipsExitWithoutThrowing()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetDouble.ExitA), target: target);
            manager.SetState(StateA);

            manager.UnregisterState(StateA);

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            Assert.AreEqual(0, target.ExitACount);
        }

        [Test]
        public void UnregisterState_UnknownState_DoesNotThrow()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();

            Assert.DoesNotThrow(() => manager.UnregisterState(999));
        }

        [Test]
        public void RegisterState_OverwritingCurrentlyActiveState_NextExitUsesTheNewestRegistration()
        {
            // _stateData is a live lookup table consulted at transition time, not a
            // snapshot frozen when a state was entered. Re-registering the currently
            // active state changes what fires on the *next* exit from it, even though
            // enter already ran (or didn't run at all here) under the old registration.
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var oldTarget = CreateBehaviour<StateTargetDouble>("Old");
            var newTarget = CreateBehaviour<StateTargetDouble>("New");
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetDouble.ExitA), target: oldTarget);
            manager.SetState(StateA);

            manager.RegisterState(StateA, exitMethod: nameof(StateTargetDouble.ExitB), target: newTarget);
            manager.SetState(StateB);

            Assert.AreEqual(0, oldTarget.ExitACount);
            Assert.AreEqual(1, newTarget.ExitBCount);
        }

        [Test]
        public void SetState_SameAsCurrentState_IsANoOp()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(StateA, nameof(StateTargetDouble.EnterA), nameof(StateTargetDouble.ExitA), target);
            manager.SetState(StateA);
            int callsAfterFirstEntry = manager.OnStateChangedCallCount;

            manager.SetState(StateA);

            Assert.AreEqual(callsAfterFirstEntry, manager.OnStateChangedCallCount);
            Assert.AreEqual(1, target.EnterACount);
            Assert.AreEqual(0, target.ExitACount);
        }

        [Test]
        public void SetState_RegisteredNegativeOneState_NeverDispatchesBecauseAlreadyCurrentAtStart()
        {
            // -1 is both the "unset" sentinel and, in principle, a registerable state
            // id. Because _currentState starts at -1, SetState(-1) short-circuits on
            // the same-state check before ever consulting _stateData, so a registered
            // state -1 can never actually be entered via SetState.
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(-1, enterMethod: nameof(StateTargetDouble.EnterA), target: target);

            manager.SetState(-1);

            Assert.AreEqual(-1, manager.CurrentState);
            Assert.AreEqual(-1, manager.PreviousState);
            Assert.AreEqual(0, target.EnterACount);
            Assert.AreEqual(0, manager.OnStateChangedCallCount);
        }

        [Test]
        public void SetState_FirstTransitionFromUnsetState_NoExitDispatched_OnlyEnter()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(StateA, nameof(StateTargetDouble.EnterA), nameof(StateTargetDouble.ExitA), target);

            manager.SetState(StateA);

            Assert.AreEqual(1, target.EnterACount);
            Assert.AreEqual(0, target.ExitACount);
        }

        [Test]
        public void SetState_TransitionBetweenTwoRegisteredStates_ExitThenEnterInOrder()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var log = new List<string>();
            var target = CreateBehaviour<StateTargetDouble>();
            target.Log = log;
            manager.RegisterState(StateA, nameof(StateTargetDouble.EnterA), nameof(StateTargetDouble.ExitA), target);
            manager.RegisterState(StateB, nameof(StateTargetDouble.EnterB), nameof(StateTargetDouble.ExitB), target);
            manager.SetState(StateA);
            log.Clear();

            manager.SetState(StateB);

            Assert.AreEqual(new[] { $"{target.name}.ExitA", $"{target.name}.EnterB" }, log.ToArray());
        }

        [Test]
        public void SetState_UpdatesCurrentAndPreviousStateFields()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();

            manager.SetState(StateA);
            Assert.AreEqual(StateA, manager.CurrentState);
            Assert.AreEqual(-1, manager.PreviousState);

            manager.SetState(StateB);
            Assert.AreEqual(StateB, manager.CurrentState);
            Assert.AreEqual(StateA, manager.PreviousState);
        }

        [Test]
        public void SetState_ChainOfTransitions_PreviousStateAlwaysTracksTheImmediatelyPriorState()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();

            manager.SetState(StateA);
            manager.SetState(StateB);
            manager.SetState(StateC);

            Assert.AreEqual(StateC, manager.CurrentState);
            Assert.AreEqual(StateB, manager.PreviousState);
        }

        [Test]
        public void SetState_UnregisteredCurrentState_SkipsExitWithoutThrowing()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            manager.SetState(StateA);

            Assert.DoesNotThrow(() => manager.SetState(StateB));
        }

        [Test]
        public void SetState_UnregisteredNewState_SkipsEnterWithoutThrowing()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();

            Assert.DoesNotThrow(() => manager.SetState(StateA));
            Assert.AreEqual(StateA, manager.CurrentState);
        }

        [Test]
        public void SetState_EmptyEnterMethodOnRegisteredState_SkipsEnterDispatch()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetDouble.ExitA), target: target);

            Assert.DoesNotThrow(() => manager.SetState(StateA));
            Assert.AreEqual(0, target.EnterACount);
        }

        [Test]
        public void SetState_EmptyExitMethodOnRegisteredState_SkipsExitDispatch()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetDouble.EnterA), target: target);
            manager.SetState(StateA);

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            Assert.AreEqual(0, target.ExitACount);
        }

        [Test]
        public void SetState_MixedTargetsAcrossStates_EachDispatchesOnlyToItsOwnTarget()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var targetA = CreateBehaviour<StateTargetDouble>("TargetA");
            var targetB = CreateBehaviour<StateTargetDouble>("TargetB");
            manager.RegisterState(StateA, nameof(StateTargetDouble.EnterA), nameof(StateTargetDouble.ExitA), targetA);
            manager.RegisterState(StateB, nameof(StateTargetDouble.EnterB), nameof(StateTargetDouble.ExitB), targetB);
            manager.SetState(StateA);

            manager.SetState(StateB);

            Assert.AreEqual(1, targetA.ExitACount);
            Assert.AreEqual(0, targetA.EnterBCount);
            Assert.AreEqual(1, targetB.EnterBCount);
            Assert.AreEqual(0, targetB.ExitACount);
        }

        [Test]
        public void SetState_OnStateChanged_ReceivesOldAndNewStateArguments()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();

            manager.SetState(StateA);
            manager.SetState(StateB);

            Assert.AreEqual(new[] { -1, StateA }, manager.OldStates.ToArray());
            Assert.AreEqual(new[] { StateA, StateB }, manager.NewStates.ToArray());
        }

        [Test]
        public void SetState_OnStateChanged_FiresBeforeEnterDispatchOfNewState()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = CreateBehaviour<StateTargetDouble>();
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetDouble.EnterA), target: target);

            manager.SetState(StateA);

            Assert.AreEqual(1, manager.OnStateChangedCallCount);
            Assert.AreEqual(1, target.EnterACount);
        }

        [Test]
        public void SetState_FullDispatchOrder_ExitThenOnStateChangedThenTsEmitThenEnter()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var log = new List<string>();
            var target = CreateBehaviour<StateTargetDouble>();
            target.Log = log;
            var externalListener = CreateBehaviour<TsvrcListenerDouble>("External");
            externalListener.Log = log;
            manager.RegisterState(StateA, nameof(StateTargetDouble.EnterA), nameof(StateTargetDouble.ExitA), target);
            manager.RegisterState(StateB, nameof(StateTargetDouble.EnterB), nameof(StateTargetDouble.ExitB), target);
            manager.TsSubscribe(externalListener, "OnStateChanged", nameof(TsvrcListenerDouble.CallbackA));
            manager.SetState(StateA);
            log.Clear();

            manager.SetState(StateB);

            Assert.AreEqual(
                new[] { $"{target.name}.ExitA", "External.CallbackA", $"{target.name}.EnterB" },
                log.ToArray());
        }

        [Test]
        public void SetState_TsEmit_NotifiesExternalTsSubscribeSubscribers()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var listener = CreateBehaviour<TsvrcListenerDouble>();
            manager.TsSubscribe(listener, "OnStateChanged", nameof(TsvrcListenerDouble.CallbackA));

            manager.SetState(StateA);
            manager.SetState(StateB);

            Assert.AreEqual(2, listener.CallbackACount);
        }

        [Test]
        public void SetState_TsEmit_DoesNotNotifySubscribersOfDifferentEventNames()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var listener = CreateBehaviour<TsvrcListenerDouble>();
            manager.TsSubscribe(listener, "SomeOtherEvent", nameof(TsvrcListenerDouble.CallbackA));

            manager.SetState(StateA);

            Assert.AreEqual(0, listener.CallbackACount);
        }

        [Test]
        public void SetState_ExitTargetGameObjectDestroyedBeforeTransition_IsSkippedSilently()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var targetGo = new GameObject("DestroyedExitTarget");
            _spawned.Add(targetGo);
            var target = targetGo.AddComponent<StateTargetDouble>();
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetDouble.ExitA), target: target);
            manager.SetState(StateA);

            Object.DestroyImmediate(targetGo);

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            Assert.AreEqual(StateB, manager.CurrentState);
        }

        [Test]
        public void SetState_EnterTargetGameObjectDestroyedBeforeTransition_IsSkippedSilently()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var targetGo = new GameObject("DestroyedEnterTarget");
            _spawned.Add(targetGo);
            var target = targetGo.AddComponent<StateTargetDouble>();
            manager.RegisterState(StateB, enterMethod: nameof(StateTargetDouble.EnterB), target: target);

            Object.DestroyImmediate(targetGo);

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            Assert.AreEqual(StateB, manager.CurrentState);
        }

        [Test]
        public void SetState_DestroyedTargetForOneState_DoesNotPreventOtherStatesFromDispatching()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var destroyedGo = new GameObject("Destroyed");
            _spawned.Add(destroyedGo);
            var destroyedTarget = destroyedGo.AddComponent<StateTargetDouble>();
            var aliveTarget = CreateBehaviour<StateTargetDouble>("Alive");
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetDouble.ExitA), target: destroyedTarget);
            manager.RegisterState(StateB, enterMethod: nameof(StateTargetDouble.EnterB), target: aliveTarget);
            manager.SetState(StateA);
            Object.DestroyImmediate(destroyedGo);

            manager.SetState(StateB);

            Assert.AreEqual(1, aliveTarget.EnterBCount);
        }

        [Test]
        public void SetState_ReentrantFromOnStateChanged_QueuedTransitionRunsAfterCurrentOneFinishes()
        {
            var manager = CreateBehaviour<StateManagerReentrantSubclass>();
            var log = new List<string>();
            var target = CreateBehaviour<StateTargetDouble>();
            target.Log = log;
            manager.RegisterState(StateA, nameof(StateTargetDouble.EnterA), nameof(StateTargetDouble.ExitA), target);
            manager.RegisterState(StateB, nameof(StateTargetDouble.EnterB), nameof(StateTargetDouble.ExitB), target);
            manager.RegisterState(StateC, nameof(StateTargetDouble.EnterC), nameof(StateTargetDouble.ExitC), target);
            manager.ArmRedirect = true;
            manager.RedirectFrom = StateB;
            manager.RedirectTo = StateC;
            manager.SetState(StateA);
            log.Clear();

            // Entering StateB triggers a reentrant SetState(StateC) from OnStateChanged.
            manager.SetState(StateB);

            // The reentrant call must be deferred until StateB's own transition (its
            // enter dispatch included) has fully finished, then run as its own complete
            // transition - not interleaved mid-way through StateB's dispatch.
            Assert.AreEqual(
                new[]
                {
                    $"{target.name}.ExitA",
                    $"{target.name}.EnterB",
                    $"{target.name}.ExitB",
                    $"{target.name}.EnterC",
                },
                log.ToArray());
            Assert.AreEqual(StateC, manager.CurrentState);
            Assert.AreEqual(StateB, manager.PreviousState);
            Assert.AreEqual(1, target.EnterBCount, "StateB's enter must fire exactly once, not re-fired stale after the nested transition.");
            Assert.AreEqual(new[] { StateA, StateB, StateC }, manager.ObservedNewStates.ToArray());
        }

        [Test]
        public void SetState_ReentrantFromEnterMethodTarget_QueuedTransitionRunsAfterCurrentOneFinishes()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var log = new List<string>();
            var redirectTarget = CreateBehaviour<StateTargetDouble>("Redirect");
            redirectTarget.Log = log;
            redirectTarget.ManagerToRedirect = manager;
            redirectTarget.RedirectTo = StateB;
            var targetB = CreateBehaviour<StateTargetDouble>("TargetB");
            targetB.Log = log;
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetDouble.EnterAndRedirect), target: redirectTarget);
            manager.RegisterState(StateB, enterMethod: nameof(StateTargetDouble.EnterB), target: targetB);

            manager.SetState(StateA);

            Assert.AreEqual(
                new[] { "Redirect.EnterAndRedirect", "TargetB.EnterB" },
                log.ToArray());
            Assert.AreEqual(StateB, manager.CurrentState);
            Assert.AreEqual(StateA, manager.PreviousState);
        }

        [Test]
        public void SetState_MultipleReentrantCallsDuringOneTransition_LastRequestedStateWins()
        {
            // The reentrancy queue is a single pending slot, not a FIFO list: if a hook
            // calls SetState more than once while a transition is already running, only
            // the most recent request survives to be drained.
            var manager = CreateBehaviour<MultiRedirectSubclass>();
            manager.RegisterState(StateA);
            manager.RegisterState(StateB);
            manager.RegisterState(StateC);
            manager.FirstRedirectTarget = StateB;
            manager.SecondRedirectTarget = StateC;
            manager.ArmOnEntering = StateA;

            manager.SetState(StateA);

            Assert.AreEqual(StateC, manager.CurrentState);
            Assert.AreEqual(StateA, manager.PreviousState, "StateB was superseded before it ever ran, so it must never appear as PreviousState.");
        }

        [Test]
        public void SetState_OnStateChangedThrows_LeavesManagerPermanentlyUnableToProcessFurtherTransitions()
        {
            // UdonSharp cannot compile try/catch/finally, so _isTransitioning's reset
            // can't be wrapped in a finally block. If OnStateChanged, a dispatched
            // enter/exit method, or an "OnStateChanged" TsEmit subscriber throws
            // mid-transition, _isTransitioning is left stuck true, and every SetState
            // call from then on is silently queued but never drained.
            var manager = CreateBehaviour<StateManagerThrowingOnStateChangedSubclass>();
            manager.ThrowWhenEnteringState = StateA;

            Assert.Throws<System.InvalidOperationException>(() => manager.SetState(StateA));
            Assert.AreEqual(StateA, manager.CurrentState, "The field update happens before OnStateChanged is invoked, so it survives the throw.");

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            Assert.AreEqual(StateA, manager.CurrentState, "A fresh call after the throw is silently swallowed instead of throwing again or succeeding.");
        }

        [Test]
        public void GetCurrentState_AndGetPreviousState_StayInSyncWithPropertiesAcrossMultipleTransitions()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();

            manager.SetState(StateA);
            manager.SetState(StateB);
            manager.SetState(StateC);

            Assert.AreEqual(manager.CurrentState, manager.GetCurrentState());
            Assert.AreEqual(manager.PreviousState, manager.GetPreviousState());
            Assert.AreEqual(StateC, manager.GetCurrentState());
            Assert.AreEqual(StateB, manager.GetPreviousState());
        }
    }

    // A subclass used only by SetState_MultipleReentrantCallsDuringOneTransition_...
    // above: on entering ArmOnEntering, issues two back-to-back reentrant SetState
    // calls, so the test can assert the single-pending-slot "last wins" queue policy.
    public class MultiRedirectSubclass : StateManager
    {
        public int ArmOnEntering = int.MinValue;
        public int FirstRedirectTarget;
        public int SecondRedirectTarget;
        private bool _armed = true;

        protected override void OnStateChanged(int oldState, int newState)
        {
            if (_armed && newState == ArmOnEntering)
            {
                _armed = false;
                SetState(FirstRedirectTarget);
                SetState(SecondRedirectTarget);
            }
        }
    }
}
