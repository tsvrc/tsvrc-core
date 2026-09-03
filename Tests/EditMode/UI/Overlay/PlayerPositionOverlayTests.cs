using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.UI;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // PlayerPositionOverlay is a PlayerTracker, so PlayerTrackerTestBase's helpers apply
    // unchanged. Everything except resolving a real player's world position is reachable
    // without ClientSim - that's covered in Tests/PlayMode/UI/Overlay instead. Pure backend:
    // every assertion here is against a RecordingPlayerMarkerRenderer test double.
    public class TsPlayerPositionOverlayTests : PlayerTrackerTestBase
    {
        private PlayerPositionOverlay CreateOverlay()
        {
            var overlay = CreateProcess<PlayerPositionOverlay>();
            overlay.Renderer = CreateComponent<RecordingPlayerMarkerRenderer>();
            return overlay;
        }

        private static RecordingPlayerMarkerRenderer GetRenderer(PlayerPositionOverlay overlay) =>
            (RecordingPlayerMarkerRenderer)overlay.Renderer;

        private static bool IsOverlayUpdating(PlayerPositionOverlay overlay) =>
            PrivateFieldAccess.GetField<bool>(overlay, "_isOverlayUpdating");

        private static void SeedRunningAndUpdating(PlayerPositionOverlay overlay)
        {
            SeedAsOwner(overlay);
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);
            PrivateFieldAccess.SetField(overlay, "_isOverlayUpdating", true);
        }

        [Test]
        public void StartOverlay_NoRenderer_LogsErrorAndDoesNotStartTracking()
        {
            var overlay = CreateProcess<PlayerPositionOverlay>();

            LogAssert.Expect(LogType.Error, "[TsVRC] [PlayerPositionOverlay] Renderer is not assigned.");
            overlay.StartOverlay(new[] { "A" });

            Assert.IsFalse(overlay.IsProcessRunning());
        }

        [Test]
        public void StartOverlay_WithRenderer_StartsTrackingGivenIdsAndBeginsUpdating()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);

            overlay.StartOverlay(new[] { "A", "B" });

            Assert.IsTrue(overlay.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "A", "B" }, overlay.LastPlayerIds);
            Assert.IsTrue(IsOverlayUpdating(overlay));
        }

        [Test]
        public void StartPlayerTracking_DirectCallNoRenderer_TracksButNeverBeginsUpdating()
        {
            // Bypasses StartOverlay's Renderer check via the inherited StartPlayerTracking;
            // OnTrackingStarted's own guard is the backstop.
            var overlay = CreateProcess<PlayerPositionOverlay>();
            SeedAsOwner(overlay);

            overlay.StartPlayerTracking(new[] { "A" });

            Assert.IsTrue(overlay.IsProcessRunning());
            Assert.IsFalse(IsOverlayUpdating(overlay));
        }

        [Test]
        public void StartPlayerTracking_DirectCallWithRendererAssigned_TracksAndBeginsUpdating()
        {
            // Proves OnTrackingStarted's own buffer allocation, not just StartOverlay's, is what
            // makes the bypass path safe.
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);

            overlay.StartPlayerTracking(new[] { "A" });

            Assert.IsTrue(overlay.IsProcessRunning());
            Assert.IsTrue(IsOverlayUpdating(overlay));
        }

        [Test]
        public void StopOverlay_Running_StopsUpdatingAndPresents()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            overlay.StartOverlay(new[] { "A" });

            overlay.StopOverlay();

            Assert.IsFalse(overlay.IsProcessRunning());
            Assert.IsFalse(IsOverlayUpdating(overlay));
            Assert.GreaterOrEqual(GetRenderer(overlay).OnPresentCount, 1);
        }

        [Test]
        public void StopOverlay_Running_FiresOnOverlayUpdatedEventEvenWithNothingDrawn()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            overlay.StartOverlay(new[] { "A" });
            var listener = CreateComponent<TsListenerDouble>();
            overlay.TsSubscribe(listener, PlayerPositionOverlay.OnOverlayUpdatedEvent, nameof(TsListenerDouble.CallbackA));

            overlay.StopOverlay();

            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void OnTrackingDeserialization_RunningNotAlreadyUpdating_StartsOverlay()
        {
            var overlay = CreateOverlay();
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);

            overlay.OnDeserialization();

            Assert.IsTrue(IsOverlayUpdating(overlay));
        }

        [Test]
        public void OnTrackingDeserialization_AlreadyUpdating_DoesNotRestart()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            int blinkCountBefore = PrivateFieldAccess.GetField<int>(overlay, "_scheduledBlinkTickCount");

            overlay.OnDeserialization();

            Assert.AreEqual(blinkCountBefore, PrivateFieldAccess.GetField<int>(overlay, "_scheduledBlinkTickCount"),
                "A deserialization event while already drawing (e.g. from an unrelated add/remove) must not restart the loop.");
        }

        [Test]
        public void OnTrackingDeserialization_NotRunning_DoesNotStartOverlay()
        {
            var overlay = CreateOverlay();

            overlay.OnDeserialization();

            Assert.IsFalse(IsOverlayUpdating(overlay));
        }

        [Test]
        public void OnTrackingDeserialization_NoRenderer_DoesNotStartOverlay()
        {
            var overlay = CreateProcess<PlayerPositionOverlay>();
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);

            Assert.DoesNotThrow(() => overlay.OnDeserialization());

            Assert.IsFalse(IsOverlayUpdating(overlay));
        }

        private static int ParsePlayerIntId(PlayerPositionOverlay overlay, string id) =>
            (int)PrivateFieldAccess.InvokeInstance(overlay, "_ParsePlayerIntId", id);

        [Test]
        public void ParsePlayerIntId_WellFormedId_ReturnsPlayerId()
        {
            var overlay = CreateOverlay();

            Assert.AreEqual(123, ParsePlayerIntId(overlay, "SomePlayer#123"));
        }

        [Test]
        public void ParsePlayerIntId_DisplayNameContainingHash_UsesTrailingHashForSuffix()
        {
            var overlay = CreateOverlay();

            Assert.AreEqual(123, ParsePlayerIntId(overlay, "My#Cool#Name#123"));
        }

        [Test]
        public void ParsePlayerIntId_ZeroId_ReturnsZero()
        {
            var overlay = CreateOverlay();

            Assert.AreEqual(0, ParsePlayerIntId(overlay, "Player#0"));
        }

        [Test]
        public void ParsePlayerIntId_MissingHash_ReturnsNegativeOne()
        {
            var overlay = CreateOverlay();

            Assert.AreEqual(-1, ParsePlayerIntId(overlay, "NoHashHere"));
        }

        [Test]
        public void ParsePlayerIntId_EmptyString_ReturnsNegativeOne()
        {
            var overlay = CreateOverlay();

            Assert.AreEqual(-1, ParsePlayerIntId(overlay, ""));
        }

        [Test]
        public void ParsePlayerIntId_HashWithNoDigitsAfter_ReturnsNegativeOne()
        {
            var overlay = CreateOverlay();

            Assert.AreEqual(-1, ParsePlayerIntId(overlay, "Player#"));
        }

        [Test]
        public void ParsePlayerIntId_NonDigitAfterHash_ReturnsNegativeOne()
        {
            var overlay = CreateOverlay();

            Assert.AreEqual(-1, ParsePlayerIntId(overlay, "Player#12a"));
        }

        [Test]
        public void OnRemoteTick_NotOverlayUpdating_IsNoOp()
        {
            var overlay = CreateOverlay();
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 1);

            Assert.DoesNotThrow(() => overlay._OnRemoteTick());

            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount"));
        }

        [Test]
        public void OnRemoteTick_StaleTickDiscardsItself()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 2);

            overlay._OnRemoteTick();

            Assert.AreEqual(1, PrivateFieldAccess.GetField<int>(overlay, "_scheduledRemoteTickCount"));
        }

        [Test]
        public void OnRemoteTick_ZeroTrackedPlayers_CachesZeroPlayers()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 1);

            overlay._OnRemoteTick();

            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount"));
        }

        [Test]
        public void OnRemoteTick_TrackedPlayersButNoneInEditModeInstance_CachesZeroWithoutThrowing()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            SetTrackedPlayerIds(overlay, new[] { "Nobody#1", "Nobody#2" });
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 1);

            Assert.DoesNotThrow(() => overlay._OnRemoteTick());

            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount"));
        }

        [Test]
        public void OnBlinkTick_NotOverlayUpdating_IsNoOp()
        {
            var overlay = CreateOverlay();
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            Assert.DoesNotThrow(() => overlay._OnBlinkTick());

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(overlay, "_markersVisible"));
            Assert.AreEqual(0, GetRenderer(overlay).OnPresentCount);
        }

        [Test]
        public void OnBlinkTick_StaleTickDiscardsItself()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 2);

            overlay._OnBlinkTick();

            Assert.AreEqual(1, PrivateFieldAccess.GetField<int>(overlay, "_scheduledBlinkTickCount"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(overlay, "_markersVisible"));
        }

        [Test]
        public void OnBlinkTick_TogglesMarkersVisibleEachCall()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            overlay._OnBlinkTick();
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(overlay, "_markersVisible"));

            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);
            overlay._OnBlinkTick();
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(overlay, "_markersVisible"));
        }

        [Test]
        public void OnBlinkTick_ShowWithNoCachedPlayers_CallsPresentButNoMarkerVisible()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);
            var listener = CreateComponent<TsListenerDouble>();
            overlay.TsSubscribe(listener, PlayerPositionOverlay.OnOverlayUpdatedEvent, nameof(TsListenerDouble.CallbackA));

            overlay._OnBlinkTick();

            Assert.AreEqual(1, listener.CallbackACount);
            Assert.AreEqual(0, GetRenderer(overlay).OnMarkerVisibleCount);
            Assert.AreEqual(1, GetRenderer(overlay).OnPresentCount);
        }

        [Test]
        public void OnBlinkTick_ShowWithCachedRemotePlayer_CallsOnMarkerVisibleWithCachedArgs()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            var cachedPos = new Vector3(3f, 0f, 4f);
            PrivateFieldAccess.SetField(overlay, "_cachedWorldPositions", new[] { cachedPos });
            PrivateFieldAccess.SetField(overlay, "_cachedHeadings", new[] { 45f });
            PrivateFieldAccess.SetField(overlay, "_cachedIsLocalArr", new[] { false });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerIdArr", new[] { "Remote#1" });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            overlay._OnBlinkTick();

            RecordingPlayerMarkerRenderer renderer = GetRenderer(overlay);
            Assert.AreEqual(1, renderer.OnMarkerVisibleCount);
            Assert.AreEqual(cachedPos, renderer.WorldPositions[0]);
            Assert.IsFalse(renderer.IsLocalPlayerArgs[0]);
            Assert.AreEqual(45f, renderer.HeadingArgs[0]);
            Assert.AreEqual("Remote#1", renderer.PlayerIdArgs[0]);
            Assert.AreEqual(1, renderer.OnPresentCount);
        }

        [Test]
        public void OnBlinkTick_ShowWithCachedLocalPlayerButNoRealLocalPlayer_SkipsLocalDrawWithoutThrowing()
        {
            // Networking.LocalPlayer is null outside Play Mode; the slot is skipped, not thrown.
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_cachedWorldPositions", new[] { Vector3.zero });
            PrivateFieldAccess.SetField(overlay, "_cachedHeadings", new float[] { 0f });
            PrivateFieldAccess.SetField(overlay, "_cachedIsLocalArr", new[] { true });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerIdArr", new[] { "Local#1" });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            Assert.DoesNotThrow(() => overlay._OnBlinkTick());

            Assert.AreEqual(0, GetRenderer(overlay).OnMarkerVisibleCount,
                "No remote markers and a skipped local draw means OnMarkerVisible was never called.");
            Assert.AreEqual(1, GetRenderer(overlay).OnPresentCount,
                "OnPresent must still be called even when nothing was drawn.");
        }

        [Test]
        public void OnBlinkTick_HideAfterShow_CallsPresentAgain()
        {
            var overlay = CreateOverlay();
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_cachedWorldPositions", new[] { Vector3.zero });
            PrivateFieldAccess.SetField(overlay, "_cachedHeadings", new float[] { 0f });
            PrivateFieldAccess.SetField(overlay, "_cachedIsLocalArr", new[] { false });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerIdArr", new[] { "Remote#1" });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);
            overlay._OnBlinkTick(); // show
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            overlay._OnBlinkTick(); // hide

            RecordingPlayerMarkerRenderer renderer = GetRenderer(overlay);
            Assert.AreEqual(1, renderer.OnMarkerVisibleCount, "The hide cycle must not call OnMarkerVisible again.");
            Assert.AreEqual(2, renderer.OnPresentCount, "OnPresent fires once per tick, show or hide.");
        }

        [Test]
        public void OnTrackingCompleted_CallsPresentAndFiresEvent()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            overlay.StartOverlay(new[] { "A" });
            int presentCountBefore = GetRenderer(overlay).OnPresentCount;
            var listener = CreateComponent<TsListenerDouble>();
            overlay.TsSubscribe(listener, PlayerPositionOverlay.OnOverlayUpdatedEvent, nameof(TsListenerDouble.CallbackA));

            overlay.CompletePlayerTracking();

            Assert.Greater(GetRenderer(overlay).OnPresentCount, presentCountBefore);
            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void ClearAndFlush_NoRenderer_NoOpWithoutThrowing()
        {
            var overlay = CreateProcess<PlayerPositionOverlay>();

            Assert.DoesNotThrow(() => PrivateFieldAccess.InvokeInstance(overlay, "_ClearAndFlush"));
        }
    }
}
