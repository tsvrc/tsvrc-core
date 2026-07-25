using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Covers AddTrackedPlayers/RemoveTrackedPlayers (the public, forwarding-aware entry
    // points) and BroadcastAddTrackedPlayers/BroadcastRemoveTrackedPlayers (the
    // [NetworkCallable] receivers that actually mutate _trackedPlayerIds).
    public class PlayerTrackerAddRemoveTests : PlayerTrackerTestBase
    {
        [Test]
        public void AddTrackedPlayers_NullArray_WarnsAndDoesNotMutate()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            LogAssert.Expect(LogType.Warning, "[TsVRC] [PlayerTrackerTestSubclass] AddTrackedPlayers called with null or empty array");
            tracker.AddTrackedPlayers(null);

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void AddTrackedPlayers_EmptyArray_WarnsAndDoesNotMutate()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            LogAssert.Expect(LogType.Warning, "[TsVRC] [PlayerTrackerTestSubclass] AddTrackedPlayers called with null or empty array");
            tracker.AddTrackedPlayers(new string[0]);

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void AddTrackedPlayers_Owner_TakesFastPathAndMutatesDirectly()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            tracker.AddTrackedPlayers(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker));
            Assert.AreEqual(1, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void AddTrackedPlayers_NotOwner_ForwardsButLocalSelfDispatchIsRejected()
        {
            // The Editor proxy's SendCustomNetworkEvent ignores NetworkEventTarget and
            // reflection-invokes the named method on the same instance synchronously, so the
            // forwarded BroadcastAddTrackedPlayers call really does execute here. This is a
            // genuine round-trip: the forwarded call must be rejected (not owner), not just
            // "doesn't throw".
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            // Deliberately not seeded as owner: _ownerPlayerIdInt/_localPlayerIdInt both 0.

            Assert.DoesNotThrow(() => tracker.AddTrackedPlayers(new[] { "A" }));

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void RemoveTrackedPlayers_NullArray_WarnsAndDoesNotMutate()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });

            LogAssert.Expect(LogType.Warning, "[TsVRC] [PlayerTrackerTestSubclass] RemoveTrackedPlayers called with null or empty array");
            tracker.RemoveTrackedPlayers(null);

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void RemoveTrackedPlayers_EmptyArray_WarnsAndDoesNotMutate()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });

            LogAssert.Expect(LogType.Warning, "[TsVRC] [PlayerTrackerTestSubclass] RemoveTrackedPlayers called with null or empty array");
            tracker.RemoveTrackedPlayers(new string[0]);

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void RemoveTrackedPlayers_Owner_TakesFastPathAndMutatesDirectly()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A", "B" });

            tracker.RemoveTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "B" }, GetTrackedPlayerIds(tracker));
            Assert.AreEqual(1, tracker.OnTrackingPlayersRemovedCount);
        }

        [Test]
        public void RemoveTrackedPlayers_NotOwner_ForwardsButLocalSelfDispatchIsRejected()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SetTrackedPlayerIds(tracker, new[] { "A" });
            // Not seeded as owner.

            Assert.DoesNotThrow(() => tracker.RemoveTrackedPlayers(new[] { "A" }));

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker),
                "A non-owner's remove request must never mutate _trackedPlayerIds locally.");
        }

        [Test]
        public void BroadcastAddTrackedPlayers_NotRunning_Rejected()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            // Never started: IsProcessRunning() is false.

            tracker.BroadcastAddTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void BroadcastAddTrackedPlayers_NotOwner_Rejected()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);
            // Flip local identity away from owner without stopping the process.
            PrivateFieldAccess.SetField(tracker, "_localPlayerIdInt", OwnerPlayerId + 1);

            tracker.BroadcastAddTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void BroadcastAddTrackedPlayers_NullOrEmpty_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            Assert.DoesNotThrow(() => tracker.BroadcastAddTrackedPlayers(null));
            Assert.DoesNotThrow(() => tracker.BroadcastAddTrackedPlayers(new string[0]));

            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void BroadcastAddTrackedPlayers_AlreadyTrackedEntries_FilteredOut()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });

            tracker.BroadcastAddTrackedPlayers(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new[] { "B" }, tracker.OnTrackingPlayersAddedArgs[0],
                "Already-tracked 'A' must be filtered out of the broadcast payload.");
        }

        [Test]
        public void BroadcastAddTrackedPlayers_AllAlreadyTracked_NoBroadcastFires()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });

            tracker.BroadcastAddTrackedPlayers(new[] { "A" });

            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void BroadcastAddTrackedPlayers_DuplicateNewIdsInOneCall_DedupedInTrackedIds()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            tracker.BroadcastAddTrackedPlayers(new[] { "A", "A" });

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker),
                "A duplicate new id within one call must not be added twice.");
        }

        [Test]
        public void BroadcastAddTrackedPlayers_DuplicateNewIdsInOneCall_DedupedInBroadcastPayload()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            tracker.BroadcastAddTrackedPlayers(new[] { "A", "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastAddedPlayerIds,
                "The broadcast payload/LastAddedPlayerIds must not carry duplicates.");
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
        }

        [Test]
        public void BroadcastRemoveTrackedPlayers_NotRunning_Rejected()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            SetTrackedPlayerIds(tracker, new[] { "A" });

            tracker.BroadcastRemoveTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void BroadcastRemoveTrackedPlayers_NotOwner_Rejected()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });
            PrivateFieldAccess.SetField(tracker, "_localPlayerIdInt", OwnerPlayerId + 1);

            tracker.BroadcastRemoveTrackedPlayers(new[] { "A" });

            CollectionAssert.AreEqual(new[] { "A" }, GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void BroadcastRemoveTrackedPlayers_NullOrEmpty_IsNoOp()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });

            Assert.DoesNotThrow(() => tracker.BroadcastRemoveTrackedPlayers(null));
            Assert.DoesNotThrow(() => tracker.BroadcastRemoveTrackedPlayers(new string[0]));

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }

        [Test]
        public void BroadcastRemoveTrackedPlayers_NotTrackedEntries_FilteredOut()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });

            tracker.BroadcastRemoveTrackedPlayers(new[] { "A", "Z" });

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new[] { "A" }, tracker.OnTrackingPlayersRemovedArgs[0],
                "Untracked 'Z' must be filtered out of the broadcast payload.");
        }

        [Test]
        public void BroadcastRemoveTrackedPlayers_NoneTracked_NoBroadcastFires()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });

            tracker.BroadcastRemoveTrackedPlayers(new[] { "Z" });

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }

        [Test]
        public void BroadcastRemoveTrackedPlayers_DuplicateIdsInOneCall_DedupedInBroadcastPayload()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A", "B" });

            tracker.BroadcastRemoveTrackedPlayers(new[] { "A", "A" });

            CollectionAssert.AreEqual(new[] { "B" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastRemovedPlayerIds,
                "LastRemovedPlayerIds must not report 'A' removed twice.");
        }

        // BroadcastAddTrackedPlayers's IsProcessRunning() guard exists specifically for the
        // window between ExecuteStop setting _isRunning=false and InternalCleanup clearing
        // _ownerId, during which IsProcessOwner() is still true. A subscriber callback from
        // OnProcessStopped/OnProcessCompleted that calls AddTrackedPlayers lands in exactly that
        // window - OnProcessStoppedAction/OnProcessCompletedAction fire after the process's own
        // broadcast but before InternalCleanup runs.

        [Test]
        public void AddTrackedPlayers_CalledFromInsideOnProcessStopped_RejectedByIsProcessRunningGuard()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });
            tracker.OnProcessStoppedAction = () => tracker.AddTrackedPlayers(new[] { "Z" });

            tracker.StopPlayerTracking();

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker),
                "The mid-cleanup-window add must be rejected, then cleanup still clears the rest.");
            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void RemoveTrackedPlayers_CalledFromInsideOnProcessStopped_RejectedByIsProcessRunningGuard()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });
            tracker.OnProcessStoppedAction = () => tracker.RemoveTrackedPlayers(new[] { "A" });

            Assert.DoesNotThrow(() => tracker.StopPlayerTracking());

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount,
                "The mid-cleanup-window remove must be rejected (IsProcessRunning() already false), " +
                "not silently succeed against the about-to-be-cleared tracked set.");
        }

        [Test]
        public void AddTrackedPlayers_CalledFromInsideOnProcessCompleted_RejectedByIsProcessRunningGuard()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });
            tracker.OnProcessCompletedAction = () => tracker.AddTrackedPlayers(new[] { "Z" });

            tracker.CompletePlayerTracking();

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
            Assert.AreEqual(0, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void RemoveTrackedPlayers_CalledFromInsideOnProcessCompleted_RejectedByIsProcessRunningGuard()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });
            tracker.OnProcessCompletedAction = () => tracker.RemoveTrackedPlayers(new[] { "A" });

            Assert.DoesNotThrow(() => tracker.CompletePlayerTracking());

            Assert.AreEqual(0, tracker.OnTrackingPlayersRemovedCount);
        }

        [Test]
        public void BroadcastAddTrackedPlayers_DirectlyCalledFromInsideOnProcessStopped_RejectedByIsProcessRunningGuard()
        {
            // Same scenario, but exercising BroadcastAddTrackedPlayers directly (the owner fast
            // path AddTrackedPlayers would take anyway) rather than through the public wrapper,
            // pinning the exact guard the source comment describes.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new[] { "A" });
            tracker.OnProcessStoppedAction = () => tracker.BroadcastAddTrackedPlayers(new[] { "Z" });

            tracker.StopPlayerTracking();

            CollectionAssert.AreEqual(new string[0], GetTrackedPlayerIds(tracker));
        }

        [Test]
        public void AddTrackedPlayers_CalledTwiceSeparately_SecondCallLastAddedOnlyReflectsSecondCallsDelta()
        {
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);

            tracker.AddTrackedPlayers(new[] { "A" });
            CollectionAssert.AreEqual(new[] { "A" }, tracker.LastAddedPlayerIds);

            tracker.AddTrackedPlayers(new[] { "B" });

            CollectionAssert.AreEqual(new[] { "B" }, tracker.LastAddedPlayerIds,
                "The second call's LastAddedPlayerIds must report only its own delta, not 'A' again.");
            CollectionAssert.AreEqual(new[] { "A", "B" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new[] { "A", "B" }, tracker.LastPlayerIds);
            Assert.AreEqual(2, tracker.OnTrackingPlayersAddedCount);
        }

        [Test]
        public void RemoveTrackedPlayers_CalledFromInsideOnTrackingPlayersAdded_EndsInConsistentState()
        {
            // A nested Broadcast*/Notify* call triggered from inside a Notify* hook: removing "A"
            // from inside OnTrackingPlayersAdded means _isBroadcasting is set, reset, and set again
            // across two overlapping call frames before everything unwinds. "A" ends up added then
            // immediately removed again; "B" survives.
            var tracker = CreateProcess<PlayerTrackerTestSubclass>();
            SeedAsOwner(tracker);
            tracker.StartPlayerTracking(new string[0]);
            tracker.OnTrackingPlayersAddedAction = () => tracker.RemoveTrackedPlayers(new[] { "A" });

            tracker.AddTrackedPlayers(new[] { "A", "B" });

            CollectionAssert.AreEqual(new[] { "B" }, GetTrackedPlayerIds(tracker));
            CollectionAssert.AreEqual(new[] { "B" }, tracker.LastPlayerIds);
            Assert.AreEqual(1, tracker.OnTrackingPlayersAddedCount);
            Assert.AreEqual(1, tracker.OnTrackingPlayersRemovedCount);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(tracker, "_isBroadcasting"),
                "_isBroadcasting must end up false once the nested call fully unwinds.");

            PrivateFieldAccess.SetField(tracker, "_isBroadcasting", false);
            tracker.NotifyTrackedPlayersAdded(new[] { "C" });
            Assert.AreEqual(1, tracker.OnTrackingPlayersAddedCount,
                "The caller-authenticity guard must not be left permanently bypassed by the reentrant sequence.");
        }
    }
}
