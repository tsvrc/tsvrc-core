using System;
using System.Collections.Generic;
using Tsvrc.Tracking;

namespace Tsvrc.Tests.EditMode
{
    // PlayerTracker inherits TsProcess's [UdonBehaviourSyncMode] attribute, so this
    // double lives in Tsvrc.Tests.Doubles for the same reason TsProcessTestSubclass
    // does (see that file's header comment) - AddComponent() silently returns null for
    // such a script when it's defined in an Editor-platform-restricted assembly.
    //
    // Records every OnTracking* hook invocation (call count + ordered args) so tests can
    // assert both "did it fire" and "with what payload", mirroring TsProcessTestSubclass's
    // CallLog/counter style.
    public class PlayerTrackerTestSubclass : PlayerTracker
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnTrackingStartedCount;
        public int OnTrackingStoppedCount;
        public int OnTrackingCompletedCount;
        public int OnTrackingDeserializationCount;
        public int OnTrackingPlayersAddedCount;
        public int OnTrackingPlayersRemovedCount;

        public readonly List<string[]> OnTrackingStartedArgs = new List<string[]>();
        public readonly List<string[]> OnTrackingStoppedArgs = new List<string[]>();
        public readonly List<string[]> OnTrackingCompletedArgs = new List<string[]>();
        public readonly List<string[]> OnTrackingPlayersAddedArgs = new List<string[]>();
        public readonly List<string[]> OnTrackingPlayersRemovedArgs = new List<string[]>();

        // Captures LastPlayerIds as observed from *inside* each hook, for ordering-guarantee
        // assertions (e.g. "LastPlayerIds already reflects the final pre-cleanup value by the
        // time OnTrackingStopped fires, not just by the time the call returns") - mirrors
        // TsProcessTestSubclass's RunningStateInsideOnProcessStopped/Completed pattern.
        public string[] LastPlayerIdsInsideOnTrackingStarted;
        public string[] LastPlayerIdsInsideOnTrackingStopped;
        public string[] LastPlayerIdsInsideOnTrackingCompleted;

        // Lets a test inject synchronous reentrant behavior (e.g. calling AddTrackedPlayers
        // from inside OnProcessStopped, to reach the documented _isRunning=false-but-not-yet-
        // cleaned-up window) without needing a fresh subclass per scenario, mirroring
        // TsProcessTestSubclass's *Action hooks. Both fire after base.OnProcessStopped/
        // Completed's own synchronous self-broadcast has already run, but before ExecuteStop/
        // ExecuteComplete's subsequent InternalCleanup call.
        public Action OnProcessStoppedAction;
        public Action OnProcessCompletedAction;

        // Lets a test inject a nested Broadcast*/Notify* call from inside OnTrackingPlayersAdded
        // itself (e.g. removing a just-added id from within the "added" hook), to exercise how
        // _isBroadcasting behaves when one self-broadcast triggers another from inside its own hook.
        public Action OnTrackingPlayersAddedAction;

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

        protected override void OnTrackingStarted(string[] playerIds)
        {
            OnTrackingStartedCount++;
            OnTrackingStartedArgs.Add(playerIds);
            LastPlayerIdsInsideOnTrackingStarted = LastPlayerIds;
            CallLog.Add("OnTrackingStarted");
        }

        protected override void OnTrackingStopped(string[] playerIds)
        {
            OnTrackingStoppedCount++;
            OnTrackingStoppedArgs.Add(playerIds);
            LastPlayerIdsInsideOnTrackingStopped = LastPlayerIds;
            CallLog.Add("OnTrackingStopped");
        }

        protected override void OnTrackingCompleted(string[] playerIds)
        {
            OnTrackingCompletedCount++;
            OnTrackingCompletedArgs.Add(playerIds);
            LastPlayerIdsInsideOnTrackingCompleted = LastPlayerIds;
            CallLog.Add("OnTrackingCompleted");
        }

        protected override void OnTrackingDeserialization()
        {
            OnTrackingDeserializationCount++;
            CallLog.Add("OnTrackingDeserialization");
        }

        protected override void OnTrackingPlayersAdded(string[] addedPlayerIds)
        {
            OnTrackingPlayersAddedCount++;
            OnTrackingPlayersAddedArgs.Add(addedPlayerIds);
            CallLog.Add("OnTrackingPlayersAdded");
            OnTrackingPlayersAddedAction?.Invoke();
        }

        protected override void OnTrackingPlayersRemoved(string[] removedPlayerIds)
        {
            OnTrackingPlayersRemovedCount++;
            OnTrackingPlayersRemovedArgs.Add(removedPlayerIds);
            CallLog.Add("OnTrackingPlayersRemoved");
        }
    }
}
