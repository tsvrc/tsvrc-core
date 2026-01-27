using Tsvrc.Core;

namespace Tsvrc.State
{
    public class StateManager : TsvrcBehaviour
    {
        protected int currentState = -1;

        protected int CurrentState
        {
            get { return currentState; }
        }

        public void SetState(int newState)
        {
            currentState = newState;
            OnStateChanged(newState);
        }

        protected virtual void OnStateChanged(int newState)
        {
            // Handle state change logic here (e.g., notify other components)
        }
    }
}