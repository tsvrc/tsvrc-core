using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tsvrc.Tests.Editor
{
    // TsPlayerPositionOverlay IS a PlayerTracker (via TsProcess), so
    // PlayerTrackerTestBase's CreateProcess<T>/SeedAsOwner/SetTrackedPlayerIds apply
    // unchanged. VRCPlayerApi.GetPlayerCount()/Networking.LocalPlayer are safe
    // (non-throwing, returning 0/null) outside Play Mode - established by the
    // PlayerTracker/AutoPlayerTracker Edit Mode suites - so everything here except
    // resolving a *real* player's world position to a pixel is reachable without
    // ClientSim. That real-position resolution is covered in
    // Tests/PlayMode/UI/TsPlayerPositionOverlay/ instead.
    public class TsPlayerPositionOverlayTests : PlayerTrackerTestBase
    {
        private TsPlayerPositionOverlay CreateOverlay()
        {
            var overlay = CreateProcess<TsPlayerPositionOverlay>();
            overlay.OverlayImage = overlay.gameObject.AddComponent<RawImage>();
            return overlay;
        }

        private static void SetupValid(TsPlayerPositionOverlay overlay, int width = 10, int height = 10, float ppu = 1f)
        {
            overlay.Setup(width, height, Vector3.zero, ppu, ppu);
        }

        private static bool IsSetUp(TsPlayerPositionOverlay overlay) =>
            PrivateFieldAccess.GetField<bool>(overlay, "_isSetup");

        private static bool IsOverlayUpdating(TsPlayerPositionOverlay overlay) =>
            PrivateFieldAccess.GetField<bool>(overlay, "_isOverlayUpdating");

        private static Texture2D GetTexture(TsPlayerPositionOverlay overlay) =>
            PrivateFieldAccess.GetField<Texture2D>(overlay, "_overlayTexture");

        private static Color32[] GetPixelBuffer(TsPlayerPositionOverlay overlay) =>
            PrivateFieldAccess.GetField<Color32[]>(overlay, "_pixelBuffer");

        private static bool AllTransparent(Color32[] pixels)
        {
            foreach (Color32 p in pixels)
                if (p.a != 0) return false;
            return true;
        }

        private static void SeedRunningAndUpdating(TsPlayerPositionOverlay overlay)
        {
            SeedAsOwner(overlay);
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);
            PrivateFieldAccess.SetField(overlay, "_isOverlayUpdating", true);
        }

        [Test]
        public void Setup_NullOverlayImage_LogsErrorAndDoesNotMarkSetup()
        {
            var overlay = CreateProcess<TsPlayerPositionOverlay>();

            LogAssert.Expect(LogType.Error, "[TsPlayerPositionOverlay] OverlayImage is not assigned.");
            overlay.Setup(10, 10, Vector3.zero, 1f, 1f);

            Assert.IsFalse(IsSetUp(overlay));
        }

        [Test]
        public void Setup_NonPositiveWidth_LogsErrorAndDoesNotMarkSetup()
        {
            var overlay = CreateOverlay();

            LogAssert.Expect(LogType.Error, "[TsPlayerPositionOverlay] Texture dimensions must be positive.");
            overlay.Setup(0, 10, Vector3.zero, 1f, 1f);

            Assert.IsFalse(IsSetUp(overlay));
        }

        [Test]
        public void Setup_NonPositiveHeight_LogsErrorAndDoesNotMarkSetup()
        {
            var overlay = CreateOverlay();

            LogAssert.Expect(LogType.Error, "[TsPlayerPositionOverlay] Texture dimensions must be positive.");
            overlay.Setup(10, -5, Vector3.zero, 1f, 1f);

            Assert.IsFalse(IsSetUp(overlay));
        }

        [Test]
        public void Setup_NonPositivePixelsPerUnitX_LogsErrorAndDoesNotMarkSetup()
        {
            var overlay = CreateOverlay();

            LogAssert.Expect(LogType.Error, "[TsPlayerPositionOverlay] PixelsPerUnit values must be positive.");
            overlay.Setup(10, 10, Vector3.zero, 0f, 1f);

            Assert.IsFalse(IsSetUp(overlay));
        }

        [Test]
        public void Setup_NonPositivePixelsPerUnitZ_LogsErrorAndDoesNotMarkSetup()
        {
            var overlay = CreateOverlay();

            LogAssert.Expect(LogType.Error, "[TsPlayerPositionOverlay] PixelsPerUnit values must be positive.");
            overlay.Setup(10, 10, Vector3.zero, 1f, -2f);

            Assert.IsFalse(IsSetUp(overlay));
        }

        [Test]
        public void Setup_Valid_AssignsTextureAndWhiteColorToOverlayImage()
        {
            var overlay = CreateOverlay();

            SetupValid(overlay);

            Assert.AreSame(GetTexture(overlay), overlay.OverlayImage.texture);
            Assert.AreEqual(Color.white, overlay.OverlayImage.color);
            Assert.IsTrue(IsSetUp(overlay));
        }

        [Test]
        public void Setup_Valid_InitialTextureIsFullyTransparent()
        {
            var overlay = CreateOverlay();

            SetupValid(overlay);

            Assert.IsTrue(AllTransparent(GetTexture(overlay).GetPixels32()));
        }

        [Test]
        public void Setup_Valid_RecachesMarkerRadiiFromCurrentInspectorFields()
        {
            var overlay = CreateOverlay();
            overlay.LocalMarkerWidth = 20f;
            overlay.LocalMarkerHeight = 10f;
            overlay.RemoteMarkerWidth = 8f;
            overlay.RemoteMarkerHeight = 8f;

            SetupValid(overlay);

            Assert.AreEqual(5, PrivateFieldAccess.GetField<int>(overlay, "_localMarkerRadius"));
            Assert.AreEqual(4, PrivateFieldAccess.GetField<int>(overlay, "_remoteMarkerRadius"));
            Assert.AreEqual(10, PrivateFieldAccess.GetField<int>(overlay, "_localHalfW"));
            Assert.AreEqual(5, PrivateFieldAccess.GetField<int>(overlay, "_localHalfH"));
        }

        [Test]
        public void Setup_ProcessAlreadyRunningButNeverStartedLocally_StartsOverlayImmediately()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);

            SetupValid(overlay);

            Assert.IsTrue(IsOverlayUpdating(overlay));
        }

        [Test]
        public void Setup_WhileRunning_RestartsAndQueuesASecondPairOfTicks()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);
            SetupValid(overlay);
            int blinkCountBeforeRestart = PrivateFieldAccess.GetField<int>(overlay, "_scheduledBlinkTickCount");
            int remoteCountBeforeRestart = PrivateFieldAccess.GetField<int>(overlay, "_scheduledRemoteTickCount");

            LogAssert.Expect(LogType.Error, new Regex("^Destroy may not be called from edit mode"));
            SetupValid(overlay, width: 20, height: 20);

            Assert.IsTrue(IsOverlayUpdating(overlay));
            Assert.Greater(PrivateFieldAccess.GetField<int>(overlay, "_scheduledBlinkTickCount"), blinkCountBeforeRestart);
            Assert.Greater(PrivateFieldAccess.GetField<int>(overlay, "_scheduledRemoteTickCount"), remoteCountBeforeRestart);
        }

        [Test]
        public void Setup_WhileRunning_TheStalePreRestartTickDiscardsItselfWhenItFires()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);
            SetupValid(overlay);
            // Both counters are 1 here (one pending blink, one pending remote tick from the
            // initial start). Restarting queues a second pair without being able to cancel
            // the first.
            LogAssert.Expect(LogType.Error, new Regex("^Destroy may not be called from edit mode"));
            SetupValid(overlay, width: 20, height: 20);
            Assert.AreEqual(2, PrivateFieldAccess.GetField<int>(overlay, "_scheduledBlinkTickCount"));
            Assert.AreEqual(2, PrivateFieldAccess.GetField<int>(overlay, "_scheduledRemoteTickCount"));

            // Firing the stale (pre-restart) tick must only decrement the counter and return,
            // not run a draw cycle.
            overlay._OnBlinkTick();
            overlay._OnRemoteTick();

            Assert.AreEqual(1, PrivateFieldAccess.GetField<int>(overlay, "_scheduledBlinkTickCount"));
            Assert.AreEqual(1, PrivateFieldAccess.GetField<int>(overlay, "_scheduledRemoteTickCount"));
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(overlay, "_markersVisible"),
                "The stale blink tick must not have toggled visibility.");
        }

        [Test]
        public void Setup_CalledTwiceNotRunning_ReplacesTextureAndBuffersCleanly()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay, width: 5, height: 5);
            Texture2D firstTexture = GetTexture(overlay);

            LogAssert.Expect(LogType.Error, new Regex("^Destroy may not be called from edit mode"));
            SetupValid(overlay, width: 8, height: 8);

            Assert.AreNotSame(firstTexture, GetTexture(overlay));
            Assert.AreEqual(8 * 8, GetPixelBuffer(overlay).Length);
        }

        [Test]
        public void Setup_OverlayFilterModeChangedAfterSetup_DoesNotAffectAlreadyCreatedTexture()
        {
            var overlay = CreateOverlay();
            overlay.OverlayFilterMode = FilterMode.Point;
            SetupValid(overlay);
            Assert.AreEqual(FilterMode.Point, GetTexture(overlay).filterMode);

            overlay.OverlayFilterMode = FilterMode.Bilinear;

            Assert.AreEqual(FilterMode.Point, GetTexture(overlay).filterMode,
                "filterMode is only applied at Setup() time, not re-read per tick.");
        }

        [Test]
        public void StartOverlay_BeforeSetup_LogsErrorAndDoesNotStartTracking()
        {
            var overlay = CreateOverlay();

            LogAssert.Expect(LogType.Error, "[TsPlayerPositionOverlay] Setup() must be called before StartOverlay().");
            overlay.StartOverlay(new[] { "A" });

            Assert.IsFalse(overlay.IsProcessRunning());
        }

        [Test]
        public void StartOverlay_AfterSetup_StartsTrackingGivenIdsAndBeginsUpdating()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            SetupValid(overlay);

            overlay.StartOverlay(new[] { "A", "B" });

            Assert.IsTrue(overlay.IsProcessRunning());
            CollectionAssert.AreEqual(new[] { "A", "B" }, overlay.LastPlayerIds);
            Assert.IsTrue(IsOverlayUpdating(overlay));
        }

        [Test]
        public void StartPlayerTracking_DirectCallWithoutSetup_TracksButNeverBeginsUpdating()
        {
            // StartOverlay()'s own _isSetup check can be bypassed by calling the inherited
            // StartPlayerTracking directly. OnTrackingStarted's own _isSetup guard is the
            // backstop that still prevents the draw loop from starting in that case.
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);

            overlay.StartPlayerTracking(new[] { "A" });

            Assert.IsTrue(overlay.IsProcessRunning());
            Assert.IsFalse(IsOverlayUpdating(overlay));
        }

        [Test]
        public void StopOverlay_Running_ClearsTextureAndStopsUpdating()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            SetupValid(overlay);
            overlay.StartOverlay(new[] { "A" });

            overlay.StopOverlay();

            Assert.IsFalse(overlay.IsProcessRunning());
            Assert.IsFalse(IsOverlayUpdating(overlay));
            Assert.IsTrue(AllTransparent(GetTexture(overlay).GetPixels32()));
        }

        [Test]
        public void StopOverlay_Running_FiresOnOverlayUpdatedEventEvenWithNothingDrawn()
        {
            var overlay = CreateOverlay();
            SeedAsOwner(overlay);
            SetupValid(overlay);
            overlay.StartOverlay(new[] { "A" });
            var listener = CreateComponent<TsListenerDouble>();
            overlay.TsSubscribe(listener, TsPlayerPositionOverlay.OnOverlayUpdatedEvent, nameof(TsListenerDouble.CallbackA));

            overlay.StopOverlay();

            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void OnTrackingDeserialization_RunningSetupNotAlreadyUpdating_StartsOverlay()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);

            overlay.OnDeserialization();

            Assert.IsTrue(IsOverlayUpdating(overlay));
        }

        [Test]
        public void OnTrackingDeserialization_AlreadyUpdating_DoesNotRestart()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
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
            SetupValid(overlay);

            overlay.OnDeserialization();

            Assert.IsFalse(IsOverlayUpdating(overlay));
        }

        [Test]
        public void OnTrackingDeserialization_NotSetup_DoesNotStartOverlay()
        {
            var overlay = CreateOverlay();
            PrivateFieldAccess.SetField(overlay, "_isRunning", true);

            Assert.DoesNotThrow(() => overlay.OnDeserialization());

            Assert.IsFalse(IsOverlayUpdating(overlay));
        }

        private static int ParsePlayerIntId(TsPlayerPositionOverlay overlay, string id) =>
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
            SetupValid(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 1);

            Assert.DoesNotThrow(() => overlay._OnRemoteTick());

            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount"));
        }

        [Test]
        public void OnRemoteTick_StaleTickDiscardsItself()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 2);

            overlay._OnRemoteTick();

            Assert.AreEqual(1, PrivateFieldAccess.GetField<int>(overlay, "_scheduledRemoteTickCount"));
        }

        [Test]
        public void OnRemoteTick_ZeroTrackedPlayers_CachesZeroPlayers()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 1);

            overlay._OnRemoteTick();

            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount"));
        }

        [Test]
        public void OnRemoteTick_TrackedPlayersButNoneInEditModeInstance_CachesZeroWithoutThrowing()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
            SeedRunningAndUpdating(overlay);
            SetTrackedPlayerIds(overlay, new[] { "Nobody#1", "Nobody#2" });
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 1);

            Assert.DoesNotThrow(() => overlay._OnRemoteTick());

            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount"));
        }

        [Test]
        public void OnRemoteTick_RecachesMarkerRadiusFromChangedInspectorField()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledRemoteTickCount", 1);
            overlay.LocalMarkerWidth = 40f;
            overlay.LocalMarkerHeight = 40f;

            overlay._OnRemoteTick();

            Assert.AreEqual(20, PrivateFieldAccess.GetField<int>(overlay, "_localMarkerRadius"));
        }

        [Test]
        public void OnBlinkTick_NotOverlayUpdating_IsNoOp()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            Assert.DoesNotThrow(() => overlay._OnBlinkTick());

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(overlay, "_markersVisible"));
        }

        [Test]
        public void OnBlinkTick_StaleTickDiscardsItself()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
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
            SetupValid(overlay);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            overlay._OnBlinkTick();
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(overlay, "_markersVisible"));

            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);
            overlay._OnBlinkTick();
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(overlay, "_markersVisible"));
        }

        [Test]
        public void OnBlinkTick_ShowWithNoCachedPlayers_EmitsEventWithoutFlushingTexture()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);
            var listener = CreateComponent<TsListenerDouble>();
            overlay.TsSubscribe(listener, TsPlayerPositionOverlay.OnOverlayUpdatedEvent, nameof(TsListenerDouble.CallbackA));

            overlay._OnBlinkTick();

            Assert.AreEqual(1, listener.CallbackACount);
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(overlay, "_bufferIsClean"));
            Assert.IsTrue(AllTransparent(GetTexture(overlay).GetPixels32()));
        }

        [Test]
        public void OnBlinkTick_ShowWithCachedRemotePlayer_DrawsRemoteColorAtCachedPixelAndFlushes()
        {
            var overlay = CreateOverlay();
            overlay.RemotePlayerColor = Color.red;
            SetupValid(overlay, width: 10, height: 10);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_cachedPxArr", new[] { 5, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            PrivateFieldAccess.SetField(overlay, "_cachedPyArr", new[] { 5, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            PrivateFieldAccess.SetField(overlay, "_cachedHeadings", new float[10]);
            PrivateFieldAccess.SetField(overlay, "_cachedIsLocalArr", new[] { false, false, false, false, false, false, false, false, false, false });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            overlay._OnBlinkTick();

            Color32[] pixels = GetTexture(overlay).GetPixels32();
            Color32 centerPixel = pixels[5 * 10 + 5];
            Assert.AreEqual((Color32)Color.red, centerPixel);
            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(overlay, "_bufferIsClean"));
        }

        [Test]
        public void OnBlinkTick_ShowWithCachedLocalPlayerButNoRealLocalPlayer_SkipsLocalDrawWithoutThrowing()
        {
            // Networking.LocalPlayer is null outside Play Mode. Documents that a tracked
            // local-player slot is silently skipped rather than throwing, matching the
            // documented "local player invalid still reflects remote-only draws correctly"
            // contract.
            var overlay = CreateOverlay();
            SetupValid(overlay);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_cachedPxArr", new[] { 0 });
            PrivateFieldAccess.SetField(overlay, "_cachedPyArr", new[] { 0 });
            PrivateFieldAccess.SetField(overlay, "_cachedHeadings", new float[] { 0f });
            PrivateFieldAccess.SetField(overlay, "_cachedIsLocalArr", new[] { true });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            Assert.DoesNotThrow(() => overlay._OnBlinkTick());

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(overlay, "_bufferIsClean"),
                "No remote markers and a skipped local draw means nothing was actually drawn.");
        }

        [Test]
        public void OnBlinkTick_HideAfterDirtyShow_ClearsTextureAndFlushes()
        {
            var overlay = CreateOverlay();
            overlay.RemotePlayerColor = Color.red;
            SetupValid(overlay, width: 10, height: 10);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_cachedPxArr", new[] { 5 });
            PrivateFieldAccess.SetField(overlay, "_cachedPyArr", new[] { 5 });
            PrivateFieldAccess.SetField(overlay, "_cachedHeadings", new float[] { 0f });
            PrivateFieldAccess.SetField(overlay, "_cachedIsLocalArr", new[] { false });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);
            overlay._OnBlinkTick(); // show
            Assert.IsFalse(AllTransparent(GetTexture(overlay).GetPixels32()));
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            overlay._OnBlinkTick(); // hide

            Assert.IsTrue(AllTransparent(GetTexture(overlay).GetPixels32()));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(overlay, "_bufferIsClean"));
        }

        [Test]
        public void OnBlinkTick_HideWhenAlreadyClean_SkipsFlushButEmitsEvent()
        {
            var overlay = CreateOverlay();
            SetupValid(overlay);
            SeedRunningAndUpdating(overlay);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);
            overlay._OnBlinkTick(); // show with nothing cached - stays clean
            var listener = CreateComponent<TsListenerDouble>();
            overlay.TsSubscribe(listener, TsPlayerPositionOverlay.OnOverlayUpdatedEvent, nameof(TsListenerDouble.CallbackA));
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            overlay._OnBlinkTick(); // hide

            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void OnBlinkTick_ShapeLiveChangedToTriangleAfterCaching_UsesHeadingCachedByThePrecedingRemoteTick()
        {
            // The blink tick reads the live RemotePlayerShape to decide circle vs triangle,
            // but the heading itself always comes from _cachedHeadings, which _OnRemoteTick
            // only populates with a real GetRotation() value when the shape was Triangle at
            // cache time. Proven here by two identical draws differing only in the cached
            // heading value, both under a live Triangle shape: different cached headings
            // must produce different pixel output, since nothing recomputes heading at draw
            // time.
            var overlayZeroHeading = CreateOverlay();
            overlayZeroHeading.RemotePlayerShape = MarkerShape.Circle;
            SetupValid(overlayZeroHeading, width: 20, height: 20);
            SeedRunningAndUpdating(overlayZeroHeading);
            PrivateFieldAccess.SetField(overlayZeroHeading, "_cachedPxArr", new[] { 10 });
            PrivateFieldAccess.SetField(overlayZeroHeading, "_cachedPyArr", new[] { 10 });
            PrivateFieldAccess.SetField(overlayZeroHeading, "_cachedHeadings", new float[] { 0f });
            PrivateFieldAccess.SetField(overlayZeroHeading, "_cachedIsLocalArr", new[] { false });
            PrivateFieldAccess.SetField(overlayZeroHeading, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlayZeroHeading, "_scheduledBlinkTickCount", 1);
            overlayZeroHeading.RemotePlayerShape = MarkerShape.Triangle;
            overlayZeroHeading._OnBlinkTick();
            Color32[] pixelsZeroHeading = GetTexture(overlayZeroHeading).GetPixels32();

            var overlayNinetyHeading = CreateOverlay();
            overlayNinetyHeading.RemotePlayerShape = MarkerShape.Circle;
            SetupValid(overlayNinetyHeading, width: 20, height: 20);
            SeedRunningAndUpdating(overlayNinetyHeading);
            PrivateFieldAccess.SetField(overlayNinetyHeading, "_cachedPxArr", new[] { 10 });
            PrivateFieldAccess.SetField(overlayNinetyHeading, "_cachedPyArr", new[] { 10 });
            PrivateFieldAccess.SetField(overlayNinetyHeading, "_cachedHeadings", new float[] { 90f });
            PrivateFieldAccess.SetField(overlayNinetyHeading, "_cachedIsLocalArr", new[] { false });
            PrivateFieldAccess.SetField(overlayNinetyHeading, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlayNinetyHeading, "_scheduledBlinkTickCount", 1);
            overlayNinetyHeading.RemotePlayerShape = MarkerShape.Triangle;
            overlayNinetyHeading._OnBlinkTick();
            Color32[] pixelsNinetyHeading = GetTexture(overlayNinetyHeading).GetPixels32();

            CollectionAssert.AreNotEqual(pixelsZeroHeading, pixelsNinetyHeading);
        }

        [Test]
        public void OnTrackingCompleted_ClearsTextureAndFiresEvent()
        {
            var overlay = CreateOverlay();
            overlay.RemotePlayerColor = Color.red;
            SetupValid(overlay, width: 10, height: 10);
            SeedAsOwner(overlay);
            overlay.StartOverlay(new[] { "A" });
            PrivateFieldAccess.SetField(overlay, "_cachedPxArr", new[] { 5 });
            PrivateFieldAccess.SetField(overlay, "_cachedPyArr", new[] { 5 });
            PrivateFieldAccess.SetField(overlay, "_cachedHeadings", new float[] { 0f });
            PrivateFieldAccess.SetField(overlay, "_cachedIsLocalArr", new[] { false });
            PrivateFieldAccess.SetField(overlay, "_cachedPlayerCount", 1);
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);
            overlay._OnBlinkTick();
            Assert.IsFalse(AllTransparent(GetTexture(overlay).GetPixels32()));
            var listener = CreateComponent<TsListenerDouble>();
            overlay.TsSubscribe(listener, TsPlayerPositionOverlay.OnOverlayUpdatedEvent, nameof(TsListenerDouble.CallbackA));

            overlay.CompletePlayerTracking();

            Assert.IsTrue(AllTransparent(GetTexture(overlay).GetPixels32()));
            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void ClearAndFlush_NeverSetup_NoOpWithoutThrowing()
        {
            var overlay = CreateOverlay();

            Assert.DoesNotThrow(() => PrivateFieldAccess.InvokeInstance(overlay, "_ClearAndFlush"));
        }
    }
}
