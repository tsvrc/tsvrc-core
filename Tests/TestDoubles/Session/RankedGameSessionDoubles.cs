using System;
using System.Collections.Generic;
using Tsvrc.Session;

namespace Tsvrc.Tests.EditMode
{
    // RankedGameSession carries [UdonBehaviourSyncMode], so this double lives in
    // Tsvrc.Tests.Doubles for the same reason PlayerTrackerTestSubclass/
    // TsProcessTestSubclass do - AddComponent() silently returns null for such a
    // script when it's defined in an Editor-platform-restricted assembly.
    //
    // Records every OnSession*/OnLobbyPlayer*/OnGamePlayer*/OnPlayerCompleted hook
    // invocation (call count + ordered CallLog + args where relevant), mirroring the
    // established style of every other test double in this suite.
    public class RankedGameSessionTestSubclass : RankedGameSession
    {
        public readonly List<string> CallLog = new List<string>();

        public int OnSessionLoadingCount;
        public int OnSessionStartedCount;
        public int OnSessionStoppedCount;
        public int OnSessionEndedCount;
        public int OnLobbyPlayerAddedCount;
        public int OnLobbyPlayerRemovedCount;
        public int OnGamePlayerRemovedCount;
        public int OnPlayerCompletedCount;

        public readonly List<string[]> OnLobbyPlayerAddedArgs = new List<string[]>();
        public readonly List<string[]> OnLobbyPlayerRemovedArgs = new List<string[]>();
        public readonly List<string[]> OnGamePlayerRemovedArgs = new List<string[]>();
        public readonly List<string[]> OnPlayerCompletedArgs = new List<string[]>();

        // Lets a test inject synchronous reentrant behavior (e.g. calling StartSession()
        // again from inside OnSessionEnded, to test an automatic next-round restart)
        // without needing a fresh subclass per scenario, mirroring the established
        // *Action hook style of every other test double in this suite.
        public Action OnSessionLoadingAction;
        public Action OnSessionStartedAction;
        public Action OnSessionStoppedAction;
        public Action OnSessionEndedAction;

        protected override void OnSessionLoading()
        {
            OnSessionLoadingCount++;
            CallLog.Add("Hook:OnSessionLoading");
            OnSessionLoadingAction?.Invoke();
        }

        protected override void OnSessionStarted()
        {
            OnSessionStartedCount++;
            CallLog.Add("Hook:OnSessionStarted");
            OnSessionStartedAction?.Invoke();
        }

        protected override void OnSessionStopped()
        {
            OnSessionStoppedCount++;
            CallLog.Add("Hook:OnSessionStopped");
            OnSessionStoppedAction?.Invoke();
        }

        protected override void OnSessionEnded()
        {
            OnSessionEndedCount++;
            CallLog.Add("Hook:OnSessionEnded");
            OnSessionEndedAction?.Invoke();
        }

        protected override void OnLobbyPlayerAdded(string[] addedIds)
        {
            OnLobbyPlayerAddedCount++;
            OnLobbyPlayerAddedArgs.Add(addedIds);
            CallLog.Add("Hook:OnLobbyPlayerAdded");
        }

        protected override void OnLobbyPlayerRemoved(string[] removedIds)
        {
            OnLobbyPlayerRemovedCount++;
            OnLobbyPlayerRemovedArgs.Add(removedIds);
            CallLog.Add("Hook:OnLobbyPlayerRemoved");
        }

        protected override void OnGamePlayerRemoved(string[] removedIds)
        {
            OnGamePlayerRemovedCount++;
            OnGamePlayerRemovedArgs.Add(removedIds);
            CallLog.Add("Hook:OnGamePlayerRemoved");
        }

        protected override void OnPlayerCompleted(string[] completedIds)
        {
            OnPlayerCompletedCount++;
            OnPlayerCompletedArgs.Add(completedIds);
            CallLog.Add("Hook:OnPlayerCompleted");
        }

        // TsSubscribe/TsEmit targets for ordering assertions.
        public void _OnSessionLoadingEventReceived() => CallLog.Add("Event:OnSessionLoadingEvent");
        public void _OnSessionStartedEventReceived() => CallLog.Add("Event:OnSessionStartedEvent");
        public void _OnSessionStoppedEventReceived() => CallLog.Add("Event:OnSessionStoppedEvent");
        public void _OnSessionEndedEventReceived() => CallLog.Add("Event:OnSessionEndedEvent");
        public void _OnLobbyPlayerAddedEventReceived() => CallLog.Add("Event:OnLobbyPlayerAddedEvent");
        public void _OnLobbyPlayerRemovedEventReceived() => CallLog.Add("Event:OnLobbyPlayerRemovedEvent");
        public void _OnGamePlayerRemovedEventReceived() => CallLog.Add("Event:OnGamePlayerRemovedEvent");
        public void _OnPlayerCompletedEventReceived() => CallLog.Add("Event:OnPlayerCompletedEvent");
        public void _OnTimerUpdatedEventReceived() => CallLog.Add("Event:OnTimerUpdatedEvent");
    }
}
