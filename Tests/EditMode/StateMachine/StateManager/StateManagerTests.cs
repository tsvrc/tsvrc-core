using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.StateMachine;
using Tsvrc.UdonTestKit;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
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
        public void RegisterState_ManagerPassedAsTarget_DispatchesOnManagerItself()
        {
            var manager = CreateBehaviour<StateManagerSelfDispatchSubclass>();
            manager.RegisterState(StateA, manager, nameof(StateManagerSelfDispatchSubclass.EnterSelf));

            manager.SetState(StateA);

            Assert.AreEqual(1, manager.EnterSelfCount);
        }

        [Test]
        public void SetState_NullTarget_DispatchSkippedSilently()
        {
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            manager.RegisterState(StateA, null, nameof(StateTargetSpy.EnterA));

            Assert.DoesNotThrow(() => manager.SetState(StateA));
            Assert.AreEqual(StateA, manager.CurrentState);
        }

        [Test]
        public void RegisterState_ExplicitTarget_DispatchesOnThatTargetNotTheManager()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetSpy.EnterA), target: target);

            manager.SetState(StateA);

            CollectionAssert.AreEqual(new[] { "Target.EnterA" }, calls);
        }

        [Test]
        public void RegisterState_NullEnterAndExitMethods_TreatedAsEmptyAndNeverDispatched()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, target: target);
            manager.RegisterState(StateB, target: target);

            manager.SetState(StateA);
            manager.SetState(StateB);

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void RegisterState_ReRegisterSameState_OverwritesPreviousEntryEntirely()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var oldTarget = calls.CreateSpy<StateTargetSpy>("Old");
            var newTarget = calls.CreateSpy<StateTargetSpy>("New");
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetSpy.EnterA), target: oldTarget);

            manager.RegisterState(StateA, enterMethod: nameof(StateTargetSpy.EnterB), target: newTarget);
            manager.SetState(StateA);

            CollectionAssert.AreEqual(new[] { "New.EnterB" }, calls, "Old registration must be fully replaced, not merged.");
        }

        [Test]
        public void UnregisterState_RemovesEntry_SubsequentTransitionSkipsExitWithoutThrowing()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetSpy.ExitA), target: target);
            manager.SetState(StateA);

            manager.UnregisterState(StateA);

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            CollectionAssert.IsEmpty(calls);
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
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var oldTarget = calls.CreateSpy<StateTargetSpy>("Old");
            var newTarget = calls.CreateSpy<StateTargetSpy>("New");
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetSpy.ExitA), target: oldTarget);
            manager.SetState(StateA);

            manager.RegisterState(StateA, exitMethod: nameof(StateTargetSpy.ExitB), target: newTarget);
            manager.SetState(StateB);

            CollectionAssert.AreEqual(new[] { "New.ExitB" }, calls);
        }

        [Test]
        public void SetState_SameAsCurrentState_IsANoOp()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, target, nameof(StateTargetSpy.EnterA), nameof(StateTargetSpy.ExitA));
            manager.SetState(StateA);
            int callsAfterFirstEntry = manager.OnStateChangedCallCount;

            manager.SetState(StateA);

            Assert.AreEqual(callsAfterFirstEntry, manager.OnStateChangedCallCount);
            CollectionAssert.AreEqual(new[] { "Target.EnterA" }, calls);
        }

        [Test]
        public void SetState_RegisteredNegativeOneState_NeverDispatchesBecauseAlreadyCurrentAtStart()
        {
            // -1 is both the "unset" sentinel and, in principle, a registerable state
            // id. Because _currentState starts at -1, SetState(-1) short-circuits on
            // the same-state check before ever consulting _stateData, so a registered
            // state -1 can never actually be entered via SetState.
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(-1, enterMethod: nameof(StateTargetSpy.EnterA), target: target);

            manager.SetState(-1);

            Assert.AreEqual(-1, manager.CurrentState);
            Assert.AreEqual(-1, manager.PreviousState);
            CollectionAssert.IsEmpty(calls);
            Assert.AreEqual(0, manager.OnStateChangedCallCount);
        }

        [Test]
        public void SetState_FirstTransitionFromUnsetState_NoExitDispatched_OnlyEnter()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, target, nameof(StateTargetSpy.EnterA), nameof(StateTargetSpy.ExitA));

            manager.SetState(StateA);

            CollectionAssert.AreEqual(new[] { "Target.EnterA" }, calls);
        }

        [Test]
        public void SetState_TransitionBetweenTwoRegisteredStates_ExitThenEnterInOrder()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, target, nameof(StateTargetSpy.EnterA), nameof(StateTargetSpy.ExitA));
            manager.RegisterState(StateB, target, nameof(StateTargetSpy.EnterB), nameof(StateTargetSpy.ExitB));
            manager.SetState(StateA);
            calls.Clear();

            manager.SetState(StateB);

            CollectionAssert.AreEqual(new[] { "Target.ExitA", "Target.EnterB" }, calls);
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
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetSpy.ExitA), target: target);

            Assert.DoesNotThrow(() => manager.SetState(StateA));
            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void SetState_EmptyExitMethodOnRegisteredState_SkipsExitDispatch()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetSpy.EnterA), target: target);
            manager.SetState(StateA);
            calls.Clear();

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void SetState_MixedTargetsAcrossStates_EachDispatchesOnlyToItsOwnTarget()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var targetA = calls.CreateSpy<StateTargetSpy>("TargetA");
            var targetB = calls.CreateSpy<StateTargetSpy>("TargetB");
            manager.RegisterState(StateA, targetA, nameof(StateTargetSpy.EnterA), nameof(StateTargetSpy.ExitA));
            manager.RegisterState(StateB, targetB, nameof(StateTargetSpy.EnterB), nameof(StateTargetSpy.ExitB));
            manager.SetState(StateA);
            calls.Clear();

            manager.SetState(StateB);

            CollectionAssert.AreEqual(new[] { "TargetA.ExitA", "TargetB.EnterB" }, calls);
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
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetSpy.EnterA), target: target);

            manager.SetState(StateA);

            Assert.AreEqual(1, manager.OnStateChangedCallCount);
            CollectionAssert.AreEqual(new[] { "Target.EnterA" }, calls);
        }

        [Test]
        public void SetState_FullDispatchOrder_ExitThenOnStateChangedThenTsEmitThenEnter()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, target, nameof(StateTargetSpy.EnterA), nameof(StateTargetSpy.ExitA));
            manager.RegisterState(StateB, target, nameof(StateTargetSpy.EnterB), nameof(StateTargetSpy.ExitB));
            manager.TsSubscribe(calls.CreateSpy("External"), "OnStateChanged", nameof(CallbackSpy.CallbackA));
            manager.SetState(StateA);
            calls.Clear();

            manager.SetState(StateB);

            CollectionAssert.AreEqual(new[] { "Target.ExitA", "External.CallbackA", "Target.EnterB" }, calls);
        }

        [Test]
        public void SetState_TsEmit_NotifiesExternalTsSubscribeSubscribers()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            manager.TsSubscribe(calls.CreateSpy("Listener"), "OnStateChanged", nameof(CallbackSpy.CallbackA));

            manager.SetState(StateA);
            manager.SetState(StateB);

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA", "Listener.CallbackA" }, calls);
        }

        [Test]
        public void SetState_TsEmit_DoesNotNotifySubscribersOfDifferentEventNames()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            manager.TsSubscribe(calls.CreateSpy("Listener"), "SomeOtherEvent", nameof(CallbackSpy.CallbackA));

            manager.SetState(StateA);

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void SetState_ExitTargetGameObjectDestroyedBeforeTransition_IsSkippedSilently()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("DestroyedExitTarget");
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetSpy.ExitA), target: target);
            manager.SetState(StateA);

            Object.DestroyImmediate(target.gameObject);

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            Assert.AreEqual(StateB, manager.CurrentState);
        }

        [Test]
        public void SetState_EnterTargetGameObjectDestroyedBeforeTransition_IsSkippedSilently()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("DestroyedEnterTarget");
            manager.RegisterState(StateB, enterMethod: nameof(StateTargetSpy.EnterB), target: target);

            Object.DestroyImmediate(target.gameObject);

            Assert.DoesNotThrow(() => manager.SetState(StateB));
            Assert.AreEqual(StateB, manager.CurrentState);
        }

        [Test]
        public void SetState_DestroyedTargetForOneState_DoesNotPreventOtherStatesFromDispatching()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var destroyedTarget = calls.CreateSpy<StateTargetSpy>("Destroyed");
            var aliveTarget = calls.CreateSpy<StateTargetSpy>("Alive");
            manager.RegisterState(StateA, exitMethod: nameof(StateTargetSpy.ExitA), target: destroyedTarget);
            manager.RegisterState(StateB, enterMethod: nameof(StateTargetSpy.EnterB), target: aliveTarget);
            manager.SetState(StateA);
            Object.DestroyImmediate(destroyedTarget.gameObject);

            manager.SetState(StateB);

            CollectionAssert.AreEqual(new[] { "Alive.EnterB" }, calls);
        }

        [Test]
        public void SetState_ReentrantFromOnStateChanged_QueuedTransitionRunsAfterCurrentOneFinishes()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerReentrantSubclass>();
            var target = calls.CreateSpy<StateTargetSpy>("Target");
            manager.RegisterState(StateA, target, nameof(StateTargetSpy.EnterA), nameof(StateTargetSpy.ExitA));
            manager.RegisterState(StateB, target, nameof(StateTargetSpy.EnterB), nameof(StateTargetSpy.ExitB));
            manager.RegisterState(StateC, target, nameof(StateTargetSpy.EnterC), nameof(StateTargetSpy.ExitC));
            manager.ArmRedirect = true;
            manager.RedirectFrom = StateB;
            manager.RedirectTo = StateC;
            manager.SetState(StateA);
            calls.Clear();

            // Entering StateB triggers a reentrant SetState(StateC) from OnStateChanged.
            manager.SetState(StateB);

            // The reentrant call must be deferred until StateB's own transition (its
            // enter dispatch included) has fully finished, then run as its own complete
            // transition - not interleaved mid-way through StateB's dispatch, and without
            // StateB's enter firing a second time.
            CollectionAssert.AreEqual(
                new[] { "Target.ExitA", "Target.EnterB", "Target.ExitB", "Target.EnterC" },
                calls);
            Assert.AreEqual(StateC, manager.CurrentState);
            Assert.AreEqual(StateB, manager.PreviousState);
            Assert.AreEqual(new[] { StateA, StateB, StateC }, manager.ObservedNewStates.ToArray());
        }

        [Test]
        public void SetState_ReentrantFromEnterMethodTarget_QueuedTransitionRunsAfterCurrentOneFinishes()
        {
            using var calls = new CallLog();
            var manager = CreateBehaviour<StateManagerTestSubclass>();
            var redirectTarget = calls.CreateSpy<StateTargetSpy>("Redirect");
            redirectTarget.ManagerToRedirect = manager;
            redirectTarget.RedirectTo = StateB;
            var targetB = calls.CreateSpy<StateTargetSpy>("TargetB");
            manager.RegisterState(StateA, enterMethod: nameof(StateTargetSpy.EnterAndRedirect), target: redirectTarget);
            manager.RegisterState(StateB, enterMethod: nameof(StateTargetSpy.EnterB), target: targetB);

            manager.SetState(StateA);

            CollectionAssert.AreEqual(new[] { "Redirect.EnterAndRedirect", "TargetB.EnterB" }, calls);
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
            manager.RegisterState(StateA, manager);
            manager.RegisterState(StateB, manager);
            manager.RegisterState(StateC, manager);
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
