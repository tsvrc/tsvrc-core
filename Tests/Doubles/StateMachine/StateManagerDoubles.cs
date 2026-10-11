using System.Collections.Generic;
using Tsvrc.StateMachine;

namespace Tsvrc.Tests.EditMode
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
