using System.Collections.Generic;
using Tsvrc.StateMachine;
using UdonSharp;

namespace Tsvrc.Tests.Editor
{
    // Records every OnStateChanged(oldState, newState) call in arrival order, so tests
    // can assert both the values passed and that it fires before TsEmit/enter dispatch.
    public class StateManagerTestSubclass : StateManager
    {
        public List<int> OldStates = new List<int>();
        public List<int> NewStates = new List<int>();
        public int OnStateChangedCallCount;

        protected override void OnStateChanged(int oldState, int newState)
        {
            OnStateChangedCallCount++;
            OldStates.Add(oldState);
            NewStates.Add(newState);
        }
    }

    // A subclass that immediately redirects to another state the first time
    // OnStateChanged observes RedirectFrom, simulating the reentrant-SetState-from-a-hook
    // pattern (e.g. "entering Loading immediately redirects to Ready if already warm").
    public class StateManagerReentrantSubclass : StateManager
    {
        public bool ArmRedirect;
        public int RedirectFrom;
        public int RedirectTo;
        public List<int> ObservedNewStates = new List<int>();

        protected override void OnStateChanged(int oldState, int newState)
        {
            ObservedNewStates.Add(newState);
            if (ArmRedirect && newState == RedirectFrom)
            {
                ArmRedirect = false;
                SetState(RedirectTo);
            }
        }
    }

    // A dispatch target for RegisterState's enter/exit methods. Tracks call counts and
    // order per named method, and can optionally call back into the manager's SetState
    // to simulate an enter/exit handler that itself triggers another transition.
    public class StateTargetDouble : UdonSharpBehaviour
    {
        public List<string> Log;
        public StateManager ManagerToRedirect;
        public int RedirectTo;

        public int EnterACount;
        public int ExitACount;
        public int EnterBCount;
        public int ExitBCount;
        public int EnterCCount;
        public int ExitCCount;

        public void EnterA() { EnterACount++; Log?.Add($"{name}.EnterA"); }
        public void ExitA() { ExitACount++; Log?.Add($"{name}.ExitA"); }
        public void EnterB() { EnterBCount++; Log?.Add($"{name}.EnterB"); }
        public void ExitB() { ExitBCount++; Log?.Add($"{name}.ExitB"); }
        public void EnterC() { EnterCCount++; Log?.Add($"{name}.EnterC"); }
        public void ExitC() { ExitCCount++; Log?.Add($"{name}.ExitC"); }

        // Registered as an enter method: redirects to RedirectTo via the manager as soon
        // as it is invoked, exercising reentrant SetState from inside a dispatched
        // enter/exit callback rather than from OnStateChanged.
        public void EnterAndRedirect()
        {
            Log?.Add($"{name}.EnterAndRedirect");
            ManagerToRedirect.SetState(RedirectTo);
        }
    }

    // A subclass that registers itself (default null target) so enter/exit dispatch
    // lands directly on the manager instance, exercising RegisterState's target-defaults-
    // to-this behavior end to end.
    public class StateManagerSelfDispatchSubclass : StateManager
    {
        public int EnterSelfCount;
        public int ExitSelfCount;

        public void EnterSelf() { EnterSelfCount++; }
        public void ExitSelf() { ExitSelfCount++; }
    }

    // Throws from OnStateChanged when entering a configured state, used to verify how
    // SetState behaves when a callback throws partway through a transition.
    public class StateManagerThrowingOnStateChangedSubclass : StateManager
    {
        public int ThrowWhenEnteringState = int.MinValue;

        protected override void OnStateChanged(int oldState, int newState)
        {
            if (newState == ThrowWhenEnteringState)
                throw new System.InvalidOperationException("Simulated OnStateChanged failure for test.");
        }
    }
}
