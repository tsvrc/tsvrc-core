using System;
using System.Collections.Generic;
using Tsvrc.Core;

namespace Tsvrc.Tests.EditMode
{
    // Process carries [UdonBehaviourSyncMode], and AddComponent() silently
    // returns null for a script with that attribute when it's defined in an
    // Editor-platform-restricted assembly — this double lives in Tsvrc.Tests.Doubles
    // instead, which has no such restriction.
    //
    // Records every subclass-hook invocation (call count + ordered call log) so tests
    // can assert both "did it fire" and "in what order relative to other hooks/state
    // changes." The *Action hooks let a test inject synchronous reentrant behavior
    // (e.g. calling StartProcess() from inside OnProcessStopped) without needing a
    // fresh subclass per scenario.
    public class ProcessTestSubclass : Process
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnProcessStartedCount;
        public int OnProcessStoppedCount;
        public int OnProcessCompletedCount;
        public int OnBecameProcessOwnerCount;
        public int OnProcessUpdateCount;
        public readonly List<bool> OnProcessCleanupArgs = new List<bool>();

        // Captures IsProcessRunning()/IsProcessOwner() as observed from inside a hook,
        // for ordering assertions (e.g. "_isRunning is already false by the time
        // OnProcessStopped fires").
        public bool? RunningStateInsideOnProcessStopped;
        public bool? RunningStateInsideOnProcessCompleted;

        public Action OnProcessStartedAction;
        public Action OnProcessStoppedAction;
        public Action OnProcessCompletedAction;
        public Action OnProcessUpdateAction;

        protected override void OnProcessStarted()
        {
            OnProcessStartedCount++;
            CallLog.Add("OnProcessStarted");
            OnProcessStartedAction?.Invoke();
        }

        protected override void OnProcessStopped()
        {
            OnProcessStoppedCount++;
            RunningStateInsideOnProcessStopped = IsProcessRunning();
            CallLog.Add("OnProcessStopped");
            OnProcessStoppedAction?.Invoke();
        }

        protected override void OnProcessCompleted()
        {
            OnProcessCompletedCount++;
            RunningStateInsideOnProcessCompleted = IsProcessRunning();
            CallLog.Add("OnProcessCompleted");
            OnProcessCompletedAction?.Invoke();
        }

        protected override void OnBecameProcessOwner()
        {
            OnBecameProcessOwnerCount++;
            CallLog.Add("OnBecameProcessOwner");
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            OnProcessCleanupArgs.Add(isCompleted);
            CallLog.Add("OnProcessCleanup:" + isCompleted);
        }

        protected override void OnProcessUpdate()
        {
            OnProcessUpdateCount++;
            CallLog.Add("OnProcessUpdate");
            OnProcessUpdateAction?.Invoke();
        }
    }

    // Proves the inherited TsvrcBehaviour pub/sub system works from within a
    // Process subclass's own hook. Lives here rather than nested inside a test
    // class for the same AddComponent/Editor-assembly reason as above.
    public class EventingProcess : ProcessTestSubclass
    {
        public const string DoneEvent = "Done";

        protected override void OnProcessCompleted()
        {
            base.OnProcessCompleted();
            TsEmit(DoneEvent);
        }
    }
}
