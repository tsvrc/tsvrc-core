using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.StateMachine
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [TsWorldExtensionPoint("TsStateManager")]
    public class StateManager : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        // -1 means no state has been set yet
        protected int _currentState = -1;
        protected int _previousState = -1;

        /// <summary>
        /// The state before the most recent transition. -1 if no transition has occurred yet.
        /// Readable by pool-based subscribers directly on the StateManager reference.
        /// </summary>
        public int PreviousState => _previousState;

        /// <summary>
        /// The currently active state. -1 if SetState has never been called.
        /// Readable by pool-based subscribers directly on the StateManager reference.
        /// </summary>
        public int CurrentState => _currentState;

        // Maps state int -> DataDictionary { "enter": methodName, "exit": methodName, "target": UdonSharpBehaviour }
        private DataDictionary _stateData = new DataDictionary();

        // A SetState call made while _isTransitioning is true (from OnStateChanged, an
        // external OnStateChanged subscriber, or a dispatched enter/exit method) is
        // queued into these fields instead of running immediately. Only the most
        // recently queued state survives if several arrive before the active
        // transition finishes.
        private bool _isTransitioning;
        private bool _hasQueuedState;
        private int _queuedState;

        /// <summary>
        /// Register a state with the behaviour its enter/exit methods are dispatched on, and their
        /// optional method names. Pass <c>this</c> as <paramref name="target"/> for a subclass that
        /// hosts the methods itself. Use nameof() at the call site for rename-safety.
        /// </summary>
        public void RegisterState(int state, UdonSharpBehaviour target, string enterMethod = null, string exitMethod = null)
        {
            var entry = new DataDictionary();
            entry["enter"] = enterMethod ?? string.Empty;
            entry["exit"] = exitMethod ?? string.Empty;
            entry["target"] = new DataToken((object)target);
            _stateData[state] = entry;
        }

        public void UnregisterState(int state)
        {
            _stateData.Remove(state);
        }

        /// <summary>
        /// Transition to newState. No-op if already in that state.
        /// Calls the registered exit method on the current state, notifies the subclass via OnStateChanged,
        /// then calls the registered enter method on the new state.
        /// SendCustomEvent dispatches to the concrete subclass.
        /// A SetState call made reentrantly (from OnStateChanged, an OnStateChanged
        /// subscriber, or an enter/exit method) is queued and runs only after the
        /// in-progress transition fully completes, rather than interleaving with it.
        /// </summary>
        public void SetState(int newState)
        {
            if (_isTransitioning)
            {
                _hasQueuedState = true;
                _queuedState = newState;
                return;
            }

            _isTransitioning = true;
            RunTransition(newState);
            _isTransitioning = false;

            while (_hasQueuedState)
            {
                _hasQueuedState = false;
                int queuedState = _queuedState;
                _isTransitioning = true;
                RunTransition(queuedState);
                _isTransitioning = false;
            }
        }

        private void RunTransition(int newState)
        {
            if (_currentState == newState)
                return;

            // currentState == -1 means no state has been entered yet, so there is
            // nothing registered to exit from.
            if (_currentState != -1 && _stateData.TryGetValue(_currentState, out DataToken exitEntry))
                Dispatch(exitEntry.DataDictionary, "exit");

            int oldState = _currentState;
            _previousState = _currentState;
            _currentState = newState;

            OnStateChanged(oldState, newState);
            TsEmit("OnStateChanged");

            if (_stateData.TryGetValue(newState, out DataToken enterEntry))
                Dispatch(enterEntry.DataDictionary, "enter");
        }

        // Skips silently when no method name was registered, or when the target is null or its
        // GameObject was destroyed since RegisterState (Unity's overridden equality reports both as
        // == null).
        private void Dispatch(DataDictionary entry, string methodKey)
        {
            string method = entry[methodKey].String;
            if (string.IsNullOrEmpty(method))
                return;

            var target = (UdonSharpBehaviour)entry["target"].Reference;
            if (target == null)
                return;

            target.SendCustomEvent(method);
        }

        public int GetCurrentState()
        {
            return _currentState;
        }

        public int GetPreviousState()
        {
            return _previousState;
        }

        /// <summary>
        /// Called after exiting the old state and before entering the new state.
        /// Override for side-effects such as animator updates.
        /// </summary>
        protected virtual void OnStateChanged(int oldState, int newState) { }
    }
}