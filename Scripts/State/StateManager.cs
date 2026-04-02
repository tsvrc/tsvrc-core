using Tsvrc.Core;
using VRC.SDK3.Data;

namespace Tsvrc.State
{
    public class StateManager : TsvrcBehaviour
    {
        // -1 means no state has been set yet
        protected int _currentState = -1;

        // Maps state int -> DataDictionary { "enter": methodName, "exit": methodName }
        private DataDictionary _stateData = new DataDictionary();

        /// <summary>
        /// Register a state with the method names to call on this behaviour when entering/exiting.
        /// Use nameof() at the call site for rename-safety.
        /// </summary>
        public void RegisterState(int state, string enterMethod = null, string exitMethod = null)
        {
            var entry = new DataDictionary();
            entry["enter"] = enterMethod ?? string.Empty;
            entry["exit"] = exitMethod ?? string.Empty;
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
                string exitMethod = _stateData[_currentState].DataDictionary["exit"].String;
                if (!string.IsNullOrEmpty(exitMethod))
                    SendCustomEvent(exitMethod);
            }

            int oldState = _currentState;
            _currentState = newState;

            // Notify subclass that the state has changed before entering the new state
            OnStateChanged(oldState, newState);

            // --- Enter new state ---
            if (_stateData.ContainsKey(newState))
            {
                string enterMethod = _stateData[newState].DataDictionary["enter"].String;
                if (!string.IsNullOrEmpty(enterMethod))
                    SendCustomEvent(enterMethod);
            }
        }

        public int GetCurrentState()
        {
            return _currentState;
        }

        /// <summary>
        /// Called after exiting the old state and before entering the new state.
        /// Override for side-effects such as animator updates.
        /// </summary>
        protected virtual void OnStateChanged(int oldState, int newState) { }
    }
}