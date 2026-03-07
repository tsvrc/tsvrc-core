using Tsvrc.Core;

namespace Tsvrc.State
{
    public class StateManager : TsvrcBehaviour
    {
        protected int currentState = -1;
        protected int[] states = new int[0];

        protected void SetState(int newState)
        {
            if (currentState == newState)
                return;

            int oldState = currentState;
            OnExitState(oldState);
            currentState = newState;
            OnEnterState(newState);
        }

        public int GetCurrentState()
        {
            return currentState;
        }

        /// <summary>
        /// Called when exiting the old state
        /// </summary>
        protected virtual void OnExitState(int oldState)
        { }

        /// <summary>
        /// Called when entering the new state
        /// </summary>
        protected virtual void OnEnterState(int newState)
        { }
    }
}