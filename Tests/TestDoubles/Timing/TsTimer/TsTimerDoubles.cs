using System;
using System.Collections.Generic;
using Tsvrc.Timing;

namespace Tsvrc.Tests.EditMode
{
    // TsTimer carries Process's [UdonBehaviourSyncMode] attribute, so this double
    // lives in Tsvrc.Tests.Doubles for the same reason PlayerTrackerTestSubclass/
    // ProcessTestSubclass do (see those files' header comments) - AddComponent()
    // silently returns null for such a script when it's defined in an Editor-platform-
    // restricted assembly.
    //
    // Records every OnTimer* hook invocation (call count + ordered CallLog) so tests can
    // assert both "did it fire" and "in what order relative to other hooks/OnTimerUpdated
    // change-detection", mirroring ProcessTestSubclass/PlayerTrackerTestSubclass's
    // established style. The *Action hooks let a test inject synchronous reentrant
    // behavior (e.g. calling StartTimer() again from inside OnTimerStopped/OnTimerCompleted)
    // without needing a fresh subclass per scenario.
    public class TsTimerTestSubclass : TsTimer
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnTimerStartedCount;
        public int OnTimerStoppedCount;
        public int OnTimerCompletedCount;
        public int OnTimerPausedCount;
        public int OnTimerResumedCount;
        public int OnTimerUpdatedCount;
        public int OnTimerDeserializationCount;

        public Action OnTimerStartedAction;
        public Action OnTimerStoppedAction;
        public Action OnTimerCompletedAction;
        public Action OnTimerPausedAction;
        public Action OnTimerResumedAction;

        protected override void OnTimerStarted()
        {
            OnTimerStartedCount++;
            CallLog.Add("Hook:OnTimerStarted");
            OnTimerStartedAction?.Invoke();
        }

        protected override void OnTimerStopped()
        {
            OnTimerStoppedCount++;
            CallLog.Add("Hook:OnTimerStopped");
            OnTimerStoppedAction?.Invoke();
        }

        protected override void OnTimerCompleted()
        {
            OnTimerCompletedCount++;
            CallLog.Add("Hook:OnTimerCompleted");
            OnTimerCompletedAction?.Invoke();
        }

        protected override void OnTimerPaused()
        {
            OnTimerPausedCount++;
            CallLog.Add("Hook:OnTimerPaused");
            OnTimerPausedAction?.Invoke();
        }

        protected override void OnTimerResumed()
        {
            OnTimerResumedCount++;
            CallLog.Add("Hook:OnTimerResumed");
            OnTimerResumedAction?.Invoke();
        }

        protected override void OnTimerDeserialization()
        {
            OnTimerDeserializationCount++;
            CallLog.Add("Hook:OnTimerDeserialization");
        }

        // TsSubscribe/TsEmit targets for ordering assertions, mirroring
        // ReadyCheckProcessTestSubclass's identically-purposed _On*EventReceived methods.
        public void _OnTimerStartedEventReceived() => CallLog.Add("Event:OnTimerStartedEvent");
        public void _OnTimerStoppedEventReceived() => CallLog.Add("Event:OnTimerStoppedEvent");
        public void _OnTimerCompletedEventReceived() => CallLog.Add("Event:OnTimerCompletedEvent");
        public void _OnTimerPausedEventReceived() => CallLog.Add("Event:OnTimerPausedEvent");
        public void _OnTimerResumedEventReceived() => CallLog.Add("Event:OnTimerResumedEvent");
        public void _OnTimerUpdatedEventReceived()
        {
            OnTimerUpdatedCount++;
            CallLog.Add("Event:OnTimerUpdatedEvent");
        }
        public void _OnTimerDeserializationEventReceived() => CallLog.Add("Event:OnTimerDeserializationEvent");
    }
}
