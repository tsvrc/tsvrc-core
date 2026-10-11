using NUnit.Framework;
using Tsvrc.Tracking;
using Tsvrc.UdonTestKit;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // AutoPlayerTracker's own new surface (on top of the inherited PlayerTracker, already
    // covered by Tests/Editor/Tracking/PlayerTracker/) is: six OnTracking*->TsEmit(OnAutoTracking*)
    // forwarders, two thin Stop/Complete forwarders, one parameter-ignoring StartPlayerTracking
    // override, and OnPlayerJoined. Every one of those is exercised here through real public
    // entry points rather than isolated reflection calls, using AutoPlayerTracker directly (no
    // test-double subclass needed - it isn't platform-restricted).
    //
    // The one thing this file cannot cover: any path that calls TsPlayer.GetAllPlayerIDs() with
    // the process NOT already running (a fresh StartAutoTracking()/StartPlayerTracking()) needs
    // a real VRCPlayerApi list, and VRCPlayerApi does not even resolve in this assembly
    // (Tsvrc.Tests.EditMode.asmdef references VRCSDKBase-Editor.dll, not VRCSDKBase.dll - same
    // constraint documented in PlayerTrackerAbandonmentTests.cs). That's covered in
    // Tests/PlayMode/Tracking/AutoPlayerTracker/ instead. The already-running restart branch of
    // both methods never reaches TsPlayer.GetAllPlayerIDs() at all after the optimization in
    // AutoPlayerTracker.StartAutoTrackingSnapshot, so it's safely testable here.
    public class AutoPlayerTrackerTests : PlayerTrackerTestBase
    {
        private static void SeedRunning(AutoPlayerTracker tracker, string[] trackedIds)
        {
            SeedAsOwner(tracker);
            PrivateFieldAccess.SetField(tracker, "_isRunning", true);
            SetTrackedPlayerIds(tracker, trackedIds);
        }

        [Test]
        public void EventConstants_HaveExpectedStringValues()
        {
            Assert.AreEqual("OnAutoTrackingStarted", AutoPlayerTracker.OnAutoTrackingStartedEvent);
            Assert.AreEqual("OnAutoTrackingStopped", AutoPlayerTracker.OnAutoTrackingStoppedEvent);
            Assert.AreEqual("OnAutoTrackingCompleted", AutoPlayerTracker.OnAutoTrackingCompletedEvent);
            Assert.AreEqual("OnAutoTrackingDeserialization", AutoPlayerTracker.OnAutoTrackingDeserializationEvent);
            Assert.AreEqual("OnAutoTrackingPlayersAdded", AutoPlayerTracker.OnAutoTrackingPlayersAddedEvent);
            Assert.AreEqual("OnAutoTrackingPlayersRemoved", AutoPlayerTracker.OnAutoTrackingPlayersRemovedEvent);
        }

        [Test]
        public void NotifyTrackedPlayersProcessStarted_Broadcasting_FiresOnAutoTrackingStartedEvent()
        {
            using var calls = new CallLog();
            var tracker = CreateProcess<AutoPlayerTracker>();
            PrivateFieldAccess.SetField(tracker, "_isBroadcasting", true);
            tracker.TsSubscribe(calls.CreateSpy("Listener"), AutoPlayerTracker.OnAutoTrackingStartedEvent, nameof(CallbackSpy.CallbackA));

            tracker.NotifyTrackedPlayersProcessStarted(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
        }

        [Test]
        public void StopAutoTracking_Running_FiresOnAutoTrackingStoppedEventAndClearsTrackedState()
        {
            using var calls = new CallLog();
            var tracker = CreateProcess<AutoPlayerTracker>();
            SeedRunning(tracker, new[] { "A", "B" });
            tracker.TsSubscribe(calls.CreateSpy("Listener"), AutoPlayerTracker.OnAutoTrackingStoppedEvent, nameof(CallbackSpy.CallbackA));

            tracker.StopAutoTracking();

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
            Assert.IsFalse(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }

        [Test]
        public void StopAutoTracking_NotRunning_LogsWarningAndFiresNoEvent()
        {
            using var calls = new CallLog();
            var tracker = CreateProcess<AutoPlayerTracker>();
            tracker.TsSubscribe(calls.CreateSpy("Listener"), AutoPlayerTracker.OnAutoTrackingStoppedEvent, nameof(CallbackSpy.CallbackA));

            LogAssert.Expect(LogType.Warning, "[TsVRC] [AutoPlayerTracker] Process is not running, ignoring stop call.");
            tracker.StopAutoTracking();

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void CompleteAutoTracking_Running_FiresOnAutoTrackingCompletedEventAndClearsTrackedState()
        {
            using var calls = new CallLog();
            var tracker = CreateProcess<AutoPlayerTracker>();
            SeedRunning(tracker, new[] { "A", "B" });
            tracker.TsSubscribe(calls.CreateSpy("Listener"), AutoPlayerTracker.OnAutoTrackingCompletedEvent, nameof(CallbackSpy.CallbackA));

            tracker.CompleteAutoTracking();

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
            Assert.IsFalse(tracker.IsProcessRunning());
            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new string[0], tracker.LastPlayerIds);
        }

        [Test]
        public void CompleteAutoTracking_NotRunning_LogsWarningAndFiresNoEvent()
        {
            using var calls = new CallLog();
            var tracker = CreateProcess<AutoPlayerTracker>();
            tracker.TsSubscribe(calls.CreateSpy("Listener"), AutoPlayerTracker.OnAutoTrackingCompletedEvent, nameof(CallbackSpy.CallbackA));

            LogAssert.Expect(LogType.Warning, "[TsVRC] [AutoPlayerTracker] Process is not running, ignoring complete call.");
            tracker.CompleteAutoTracking();

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void OnDeserialization_FiresOnAutoTrackingDeserializationEventAndRefreshesLastPlayerIds()
        {
            using var calls = new CallLog();
            var tracker = CreateProcess<AutoPlayerTracker>();
            SetTrackedPlayerIds(tracker, new[] { "A" });
            tracker.TsSubscribe(calls.CreateSpy("Listener"), AutoPlayerTracker.OnAutoTrackingDeserializationEvent, nameof(CallbackSpy.CallbackA));

            tracker.OnDeserialization();

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastPlayerIds);
        }

        [Test]
        public void AddTrackedPlayers_Owner_FiresOnAutoTrackingPlayersAddedEvent()
        {
            using var calls = new CallLog();
            var tracker = CreateProcess<AutoPlayerTracker>();
            SeedRunning(tracker, new string[0]);
            tracker.TsSubscribe(calls.CreateSpy("Listener"), AutoPlayerTracker.OnAutoTrackingPlayersAddedEvent, nameof(CallbackSpy.CallbackA));

            tracker.AddTrackedPlayers(new[] { "X", "Y" });

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
            CollectionAssert.AreEqual(new[] { "X", "Y" }, tracker.LastAddedPlayerIds);
            CollectionAssert.AreEqual(new[] { "X", "Y" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void RemoveTrackedPlayers_Owner_FiresOnAutoTrackingPlayersRemovedEvent()
        {
            using var calls = new CallLog();
            var tracker = CreateProcess<AutoPlayerTracker>();
            SeedRunning(tracker, new[] { "X", "Y" });
            tracker.TsSubscribe(calls.CreateSpy("Listener"), AutoPlayerTracker.OnAutoTrackingPlayersRemovedEvent, nameof(CallbackSpy.CallbackA));

            tracker.RemoveTrackedPlayers(new[] { "X" });

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
            CollectionAssert.AreEqual(new[] { "X" }, tracker.LastRemovedPlayerIds);
            CollectionAssert.AreEqual(new[] { "Y" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void StartAutoTracking_AlreadyRunning_SkipsPlayerSnapshotAndDoesNotMutateTrackedState()
        {
            // The already-running branch never reaches TsPlayer.GetAllPlayerIDs() (which needs
            // a real VRCPlayerApi list and would not compile/run meaningfully in this Edit
            // Mode assembly), so this is safely callable here even though StartAutoTracking's
            // fresh-start branch is not.
            var tracker = CreateProcess<AutoPlayerTracker>();
            SeedRunning(tracker, new[] { "A", "B" });

            LogAssert.Expect(LogType.Warning, "[TsVRC] [AutoPlayerTracker] Process is already running, ignoring start call.");
            Assert.DoesNotThrow(() => tracker.StartAutoTracking());

            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void StartPlayerTracking_AlreadyRunning_SkipsPlayerSnapshotAndIgnoresCustomArray()
        {
            var tracker = CreateProcess<AutoPlayerTracker>();
            SeedRunning(tracker, new[] { "A" });

            LogAssert.Expect(LogType.Warning, "[TsVRC] [AutoPlayerTracker] Process is already running, ignoring start call.");
            Assert.DoesNotThrow(() => tracker.StartPlayerTracking(new[] { "Z", "Y" }, true));

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
        }
    }
}
