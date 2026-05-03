using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.State
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StateManager : TsvrcBehaviour
    {
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

        /// <summary>
        /// Register a state with optional enter/exit method names and an optional target behaviour to
        /// dispatch them on. If target is null, dispatches on this StateManager (for subclass overrides).
        /// Use nameof() at the call site for rename-safety.
        /// </summary>
        public void RegisterState(int state, string enterMethod = null, string exitMethod = null, UdonSharpBehaviour target = null)
        {
            var entry = new DataDictionary();
            entry["enter"] = enterMethod ?? string.Empty;
            entry["exit"] = exitMethod ?? string.Empty;
            entry["target"] = target != null ? new DataToken((object)target) : new DataToken((object)this);
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
        /// </summary>
        public void SetState(int newState)
        {
            if (_currentState == newState)
                return;

            // --- Exit current state ---
            // Only call exit if a state was previously set and it has a registered handler.
            // currentState == -1 means no state has been entered yet (initial value).
            if (_currentState != -1 && _stateData.ContainsKey(_currentState))
            {
                var exitEntry = _stateData[_currentState].DataDictionary;
                string exitMethod = exitEntry["exit"].String;
                if (!string.IsNullOrEmpty(exitMethod))
                {
                    var exitTarget = (UdonSharpBehaviour)exitEntry["target"].Reference;
                    exitTarget.SendCustomEvent(exitMethod);
                }
            }

            int oldState = _currentState;
            _previousState = _currentState;
            _currentState = newState;

            // Notify subclass that the state has changed before entering the new state
            OnStateChanged(oldState, newState);

            // Notify any external subscribers (compose-based users)
            TsEmit("OnStateChanged");

            // --- Enter new state ---
            if (_stateData.ContainsKey(newState))
            {
                var enterEntry = _stateData[newState].DataDictionary;
                string enterMethod = enterEntry["enter"].String;
                if (!string.IsNullOrEmpty(enterMethod))
                {
                    var enterTarget = (UdonSharpBehaviour)enterEntry["target"].Reference;
                    enterTarget.SendCustomEvent(enterMethod);
                }
            }
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