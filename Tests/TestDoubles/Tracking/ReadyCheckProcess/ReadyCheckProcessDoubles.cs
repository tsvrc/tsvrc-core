using System;
using System.Collections.Generic;
using Tsvrc.Tracking;

namespace Tsvrc.Tests.EditMode
{
    // ReadyCheckProcess inherits Process's [UdonBehaviourSyncMode] attribute (through
    // PlayerTracker), so this double lives in Tsvrc.Tests.Doubles for the same reason
    // PlayerTrackerTestSubclass/ProcessTestSubclass do - AddComponent() silently returns
    // null for such a script when it's defined in an Editor-platform-restricted assembly.
    //
    // Records every OnReadyCheck* hook invocation (call count + ordered args where relevant)
    // and the OnTrackingPlayersRemoved override so tests can assert both "did it fire" and
    // "in what order relative to the inherited PlayerTracker/Process events", mirroring
    // PlayerTrackerTestSubclass's CallLog/counter style.
    public class ReadyCheckProcessTestSubclass : ReadyCheckProcess
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnReadyCheckStartedCount;
        public int OnReadyCheckStoppedCount;
        public int OnReadyCheckCompletedCount;

        // Captures LastPlayerIds (inherited from PlayerTracker) as observed from *inside* each
        // ReadyCheck-specific hook, mirroring PlayerTrackerTestSubclass's own
        // LastPlayerIdsInsideOnTracking*/CallLog pattern - every OnReadyCheck*Event's own doc
        // comment says "Read LastPlayerIds in your callback", so this pins that the data is
        // actually already correct at that point, not just that the hooks fire in order.
        public string[] LastPlayerIdsInsideOnReadyCheckStarted;
        public string[] LastPlayerIdsInsideOnReadyCheckStopped;
        public string[] LastPlayerIdsInsideOnReadyCheckCompleted;

        public int OnTrackingPlayersRemovedCount;
        public readonly List<string[]> OnTrackingPlayersRemovedArgs = new List<string[]>();

        // Lets a test inject synchronous reentrant behavior from the earliest possible point
        // inside the ReadyCheck-specific hooks (before the inherited OnTracking*Event and
        // OnReadyCheck*Event have fired), to exercise the "restart before base has finished
        // broadcasting" window described in ReadyCheckProcess.OnProcessStarted's own comment.
        public Action OnReadyCheckStartedAction;
        public Action OnReadyCheckStoppedAction;
        public Action OnReadyCheckCompletedAction;

        // Lets a test inject reentrant behavior from the latest possible point - after the
        // full inherited PlayerTracker.OnProcessStopped/Completed broadcast (including every
        // OnReadyCheck*/OnTracking* hook and event above) has already returned, but still
        // before Process.InternalCleanup runs. Mirrors PlayerTrackerTestSubclass's
        // identically-named hooks, one level up the inheritance chain.
        public Action OnProcessStoppedAction;
        public Action OnProcessCompletedAction;

        protected override void OnProcessStopped()
        {
            base.OnProcessStopped();
            OnProcessStoppedAction?.Invoke();
        }

        protected override void OnProcessCompleted()
        {
            base.OnProcessCompleted();
            OnProcessCompletedAction?.Invoke();
        }

        protected override void OnReadyCheckStarted()
        {
            OnReadyCheckStartedCount++;
            LastPlayerIdsInsideOnReadyCheckStarted = LastPlayerIds;
            CallLog.Add("Hook:OnReadyCheckStarted");
            OnReadyCheckStartedAction?.Invoke();
        }

        protected override void OnReadyCheckStopped()
        {
            OnReadyCheckStoppedCount++;
            LastPlayerIdsInsideOnReadyCheckStopped = LastPlayerIds;
            CallLog.Add("Hook:OnReadyCheckStopped");
            OnReadyCheckStoppedAction?.Invoke();
        }

        protected override void OnReadyCheckCompleted()
        {
            OnReadyCheckCompletedCount++;
            LastPlayerIdsInsideOnReadyCheckCompleted = LastPlayerIds;
            CallLog.Add("Hook:OnReadyCheckCompleted");
            OnReadyCheckCompletedAction?.Invoke();
        }

        protected override void OnTrackingPlayersRemoved(string[] removedPlayerIds)
        {
            base.OnTrackingPlayersRemoved(removedPlayerIds);
            OnTrackingPlayersRemovedCount++;
            OnTrackingPlayersRemovedArgs.Add(removedPlayerIds);
            CallLog.Add("Hook:OnTrackingPlayersRemoved");
        }

        // TsSubscribe/TsEmit targets for ordering assertions: proves OnReadyCheckStartedEvent
        // fires before OnTrackingStartedEvent for the same transition (ReadyCheckProcess's
        // override calls OnReadyCheckStarted()+TsEmit(OnReadyCheckStartedEvent) from inside
        // PlayerTracker.NotifyTrackedPlayersProcessStarted's own call to OnTrackingStarted,
        // strictly before that outer method's own TsEmit(OnTrackingStartedEvent) call).
        public void _OnReadyCheckStartedEventReceived() => CallLog.Add("Event:OnReadyCheckStartedEvent");
        public void _OnTrackingStartedEventReceived() => CallLog.Add("Event:OnTrackingStartedEvent");
        public void _OnReadyCheckStoppedEventReceived() => CallLog.Add("Event:OnReadyCheckStoppedEvent");
        public void _OnTrackingStoppedEventReceived() => CallLog.Add("Event:OnTrackingStoppedEvent");
        public void _OnReadyCheckCompletedEventReceived() => CallLog.Add("Event:OnReadyCheckCompletedEvent");
        public void _OnTrackingCompletedEventReceived() => CallLog.Add("Event:OnTrackingCompletedEvent");
    }
}
