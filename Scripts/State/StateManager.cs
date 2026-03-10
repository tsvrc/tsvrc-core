using Tsvrc.Core;
using VRC.SDK3.Data;

namespace Tsvrc.State
{
    public class StateManager : TsvrcBehaviour
    {
        // -1 means no state has been set yet
        protected int _currentState = -1;

        // Maps state int -> DataDictionary { "load": methodName, "unload": methodName }
        private DataDictionary _stateData = new DataDictionary();

        /// <summary>
        /// Register a state with the method names to call on this behaviour when entering/exiting.
        /// Use nameof() at the call site for rename-safety.
        /// </summary>
        public void RegisterState(int state, string loadMethod, string unloadMethod)
        {
            var entry = new DataDictionary();
            entry["load"] = loadMethod;
            entry["unload"] = unloadMethod;
            _stateData[state] = entry;
        }

        public void UnregisterState(int state)
        {
            _stateData.Remove(state);
        }

        /// <summary>
        /// Transition to newState. No-op if already in that state.
        /// Calls the registered unload method on the current state, then the load method on the new state.
        /// SendCustomEvent dispatches to the concrete subclass.
        /// </summary>
        public void SetState(int newState)
        {
            if (_currentState == newState)
                return;

            // --- Exit current state ---
            // Only call unload if a state was previously set and it has a registered handler.
            // currentState == -1 means no state has been entered yet (initial value).
            if (_currentState != -1 && _stateData.ContainsKey(_currentState))
            {
                string unloadMethod = _stateData[_currentState].DataDictionary["unload"].String;
                SendCustomEvent(unloadMethod);
            }

            int oldState = _currentState;
            _currentState = newState;

            // --- Enter new state ---
            if (_stateData.ContainsKey(newState))
            {
                string loadMethod = _stateData[newState].DataDictionary["load"].String;
                SendCustomEvent(loadMethod);
            }

            // Notify subclass after the transition is fully committed
            OnStateChanged(oldState, newState);
        }

        public int GetCurrentState()
        {
            return _currentState;
        }

        /// <summary>
        /// Called after every state transition. Override for side-effects such as animator updates.
        /// </summary>
        protected virtual void OnStateChanged(int oldState, int newState) { }
    }
}