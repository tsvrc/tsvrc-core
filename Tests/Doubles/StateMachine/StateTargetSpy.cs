using Tsvrc.StateMachine;
using Tsvrc.UdonTestKit;

namespace Tsvrc.Tests.EditMode
{
    // A dispatch target for RegisterState's enter/exit methods, recording each call in the
    // CallLog that created it.
    public class StateTargetSpy : CallbackSpy
    {
        public StateManager ManagerToRedirect;
        public int RedirectTo;

        public void EnterA() => Record();
        public void ExitA() => Record();
        public void EnterB() => Record();
        public void ExitB() => Record();
        public void EnterC() => Record();
        public void ExitC() => Record();

        // An enter method that itself calls SetState, for reentrant transitions started from
        // inside a dispatched enter/exit callback rather than from OnStateChanged.
        public void EnterAndRedirect()
        {
            Record();
            ManagerToRedirect.SetState(RedirectTo);
        }
    }
}
