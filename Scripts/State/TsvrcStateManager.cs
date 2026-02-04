using Tsvrc.Core;

namespace Tsvrc.State
{
    public class TsvrcStateManager : TsvrcBehaviour
    {
        protected int currentState = -1;

        protected void SetState(int newState)
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