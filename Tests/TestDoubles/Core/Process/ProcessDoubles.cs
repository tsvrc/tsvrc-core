using System;
using System.Collections.Generic;
using Tsvrc.Core;

namespace Tsvrc.Tests.Doubles
{
    // Used by both EditMode and PlayMode
    // Process tests, and PlayMode's asmdef doesn't reference the EditMode assembly.
    //
    // Tracks hook call counts and order via CallLog. The *Action hooks let a test inject
    // reentrant behavior (e.g. StartProcess() from inside OnProcessStopped) without a new subclass.
    public class ProcessTestSubclass : Process
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnProcessStartedCount;
        public int OnProcessStoppedCount;
        public int OnProcessCompletedCount;
        public int OnBecameProcessOwnerCount;
        public int OnProcessUpdateCount;
        public readonly List<bool> OnProcessCleanupArgs = new List<bool>();

        // IsProcessRunning() as seen from inside the hook, e.g. to confirm _isRunning
        // is already false by the time OnProcessStopped fires.
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

    // Proves TsvrcBehaviour's pub/sub works from inside a Process subclass hook.
    // Lives here, not nested in a test class, for the same reason as above.
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
