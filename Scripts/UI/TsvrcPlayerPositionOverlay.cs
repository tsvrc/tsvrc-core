using Tsvrc.Network;
using Tsvrc.Player;
using Tsvrc.UI.Utils;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Tsvrc.UI
{
    /// <summary>
    /// Shape used to draw a player marker in the <see cref="TsvrcPlayerPositionOverlay"/> texture.
    /// </summary>
    public enum MarkerShape
    {
        /// <summary>Filled circle. Size is controlled by the marker width and height fields.</summary>
        Circle = 0,
        /// <summary>Filled triangle that rotates to match the player's facing direction.</summary>
        Triangle = 1,
    }

    /// <summary>
    /// Projects VRChat player world positions onto a 2D <see cref="RawImage"/> overlay texture.
    /// Extends <see cref="PlayerTracker"/>, so the owner controls which players are shown and all
    /// clients receive network events when the tracked set changes.
    ///
    /// Every client runs its own local draw loop. No rendering state is synced over the network.
    ///
    /// Typical usage:
    /// <list type="number">
    ///   <item>Call <see cref="Setup"/> to configure texture dimensions and world mapping.
    ///         Optional if inspector defaults are acceptable.</item>
    ///   <item>Call <see cref="StartOverlay"/> from the owner to begin tracking and drawing.</item>
    ///   <item>Use <see cref="PlayerTracker.AddTrackedPlayers"/> and
    ///         <see cref="PlayerTracker.RemoveTrackedPlayers"/> to change the displayed set at runtime.</item>
    ///   <item>Call <see cref="StopOverlay"/> to end the process and clear the overlay on all clients.</item>
    /// </list>
    ///
    /// Subscribe to <see cref="OnOverlayUpdatedEvent"/> via <c>TsSubscribe</c> to receive a
    /// callback after each draw cycle.
    ///
    /// Local and remote players each support an independent marker shape and color.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcPlayerPositionOverlay : PlayerTracker
    {
        /// <summary>
        /// Emitted via <see cref="TsvrcBehaviour.TsEmit"/> after every draw cycle completes.
        /// Read the updated texture from <see cref="OverlayImage"/> inside your callback.
        /// </summary>
        public const string OnOverlayUpdatedEvent = "OnOverlayUpdated";

        [Header("Display")]
        [Tooltip("RawImage that receives the generated overlay texture.")]
        public RawImage OverlayImage;

        [Tooltip(
            "Filter mode for the generated overlay texture.\n" +
            "Point = crisp pixel art, best for minimaps.\n" +
            "Bilinear / Trilinear = smooth when the RawImage is scaled larger than the texture.")]
        public FilterMode OverlayFilterMode = FilterMode.Point;

        [Header("Texture Setup")]
        [Tooltip("Overlay texture width in pixels. Used when Setup() is not called before StartOverlay().")]
        [Min(1)] public int TextureWidth = 512;

        [Tooltip("Overlay texture height in pixels. Used when Setup() is not called before StartOverlay().")]
        [Min(1)] public int TextureHeight = 512;

        [Tooltip("World-space bottom-left corner of the mapped area (XZ plane). Pixel (0,0) maps here.")]
        public Vector3 WorldOrigin;

        [Header("Scale")]
        [Tooltip("Pixels per world unit along the X axis. Changes take effect on the next tick.")]
        [Min(0.001f)] public float PixelsPerUnitX = 1f;

        [Tooltip("Pixels per world unit along the Z axis. Changes take effect on the next tick.")]
        [Min(0.001f)] public float PixelsPerUnitZ = 1f;

        [Header("Local Player")]
        [Tooltip("Shape used to draw the local player marker in the overlay texture.")]
        public MarkerShape LocalPlayerShape = MarkerShape.Circle;
        public Color LocalPlayerColor = Color.yellow;
        [Tooltip("Local player marker width in texture pixels. Changes take effect on the next tick.")]
        [Min(1f)] public float LocalMarkerWidth = 12f;
        [Tooltip("Local player marker height in texture pixels. Changes take effect on the next tick.")]
        [Min(1f)] public float LocalMarkerHeight = 12f;

        [Header("Remote Players")]
        [Tooltip("Shape used to draw remote player markers in the overlay texture.")]
        public MarkerShape RemotePlayerShape = MarkerShape.Circle;
        public Color RemotePlayerColor = Color.red;
        [Tooltip("Remote player marker width in texture pixels. Changes take effect on the next tick.")]
        [Min(1f)] public float RemoteMarkerWidth = 12f;
        [Tooltip("Remote player marker height in texture pixels. Changes take effect on the next tick.")]
        [Min(1f)] public float RemoteMarkerHeight = 12f;

        [Header("Performance")]
        [Tooltip("How often the texture refreshes for the local player, in seconds. Lower values make your own marker more responsive.")]
        [Min(0.05f)] public float LocalUpdateInterval = 0.1f;

        [Tooltip("How often the texture refreshes for remote players, in seconds.")]
        [Min(0.05f)] public float RemoteUpdateInterval = 0.3f;

        [Tooltip(
          "How many update cycles a marker trail takes to fade completely.\n" +
          "Fade time in seconds ≈ FadeMultiplier × min(LocalUpdateInterval, RemoteUpdateInterval).\n" +
          "Higher values = longer sonar trails. Lower values = snappier, shorter trails.")]
        [Min(1f)] public float FadeMultiplier = 4f;

        private Texture2D _overlayTexture;
        private int _textureWidth;
        private int _textureHeight;
        private Vector3 _worldOrigin;
        private float _pixelsPerUnitX;
        private float _pixelsPerUnitZ;
        // Circle radius is derived from the marker width and height each tick,
        // so changes made in the inspector take effect without a new Setup() call.
        private int _localMarkerRadius;
        private int _remoteMarkerRadius;

        // Pre-allocated buffers. Reused every tick to avoid GC pressure.
        private Color32[] _pixelBuffer;
        private VRCPlayerApi[] _playerBuffer; // VRChat caps an instance at 82 players

        // These are recached at the start of each tick so inspector edits take effect immediately.
        private Color32 _localColor32;
        private Color32 _remoteColor32;
        private Vector2 _overlayRectSize;
        // Per-channel multiplier for the sonar fade. Applied as (channel * _decayFactor) >> 8
        // so the decay loop stays integer-only and avoids float math per pixel.
        private byte _decayFactor;

        private bool _isSetup;
        private bool _isOverlayUpdating;

        // Double-tick guard: each schedule call increments the counter, each fired event decrements it.
        // If the counter is still above zero when a tick fires, a newer tick is already queued
        // and this one is stale. Discarding it prevents duplicate draw cycles.
        private int _scheduledLocalTickCount;
        private int _scheduledRemoteTickCount;

        /// <summary>
        /// Configures the overlay texture dimensions and world-to-pixel mapping.
        /// Call this on every client independently, either before or after <see cref="StartOverlay"/>.
        /// If the overlay is already running, it restarts immediately with the new configuration.
        /// Safe to call multiple times.
        /// </summary>
        /// <param name="width">Overlay texture width in pixels.</param>
        /// <param name="height">Overlay texture height in pixels.</param>
        /// <param name="worldOrigin">World-space position that maps to pixel (0, 0). Represents the bottom-left corner of the mapped area on the XZ plane.</param>
        /// <param name="pixelsPerUnitX">Pixels per world unit along the X axis.</param>
        /// <param name="pixelsPerUnitZ">Pixels per world unit along the Z axis.</param>
        public void Setup(int width, int height, Vector3 worldOrigin, float pixelsPerUnitX, float pixelsPerUnitZ)
        {
            if (OverlayImage == null)
            {
                Debug.LogError("[TsvrcPlayerPositionOverlay] OverlayImage is not assigned.");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogError("[TsvrcPlayerPositionOverlay] Texture dimensions must be positive.");
                return;
            }

            if (pixelsPerUnitX <= 0f || pixelsPerUnitZ <= 0f)
            {
                Debug.LogError("[TsvrcPlayerPositionOverlay] PixelsPerUnit values must be positive.");
                return;
            }

            _textureWidth = width;
            _textureHeight = height;
            _worldOrigin = worldOrigin;
            // Write back to the inspector fields so they reflect the active configuration
            // and the auto-setup fallback in StartOverlay picks up the right values on re-entry.
            PixelsPerUnitX = pixelsPerUnitX;
            PixelsPerUnitZ = pixelsPerUnitZ;
            _pixelsPerUnitX = pixelsPerUnitX;
            _pixelsPerUnitZ = pixelsPerUnitZ;
            _localMarkerRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(LocalMarkerWidth, LocalMarkerHeight) / 2f));
            _remoteMarkerRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(RemoteMarkerWidth, RemoteMarkerHeight) / 2f));
            _localColor32 = LocalPlayerColor;
            _remoteColor32 = RemotePlayerColor;

            if (_overlayTexture != null)
                Destroy(_overlayTexture);

            _overlayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _overlayTexture.filterMode = OverlayFilterMode;

            // Color32 defaults to (0,0,0,0) which is fully transparent, so no explicit fill is needed.
            _pixelBuffer = new Color32[width * height];
            _overlayTexture.SetPixels32(_pixelBuffer);
            _overlayTexture.Apply();

            OverlayImage.texture = _overlayTexture;
            OverlayImage.color = Color.white;

            if (_playerBuffer == null)
                _playerBuffer = new VRCPlayerApi[82];

            _overlayRectSize = OverlayImage.rectTransform.rect.size;

            _isSetup = true;

            if (IsProcessRunning())
                _StartOverlay();
        }

        /// <summary>
        /// Starts the overlay and begins tracking the given player IDs.
        /// If <see cref="Setup"/> was not called first, inspector values are used as defaults.
        /// </summary>
        public void StartOverlay(string[] playerIds)
        {
            if (!_isSetup)
                Setup(TextureWidth, TextureHeight, WorldOrigin, PixelsPerUnitX, PixelsPerUnitZ);
            StartPlayerTracking(playerIds);
        }

        /// <summary>
        /// Stops the overlay and clears the texture on all clients.
        /// </summary>
        public void StopOverlay()
        {
            StopPlayerTracking();
        }

        protected override void OnTrackingStarted(string[] playerIds)
        {
            if (!_isSetup)
                Setup(TextureWidth, TextureHeight, WorldOrigin, PixelsPerUnitX, PixelsPerUnitZ);
            // Setup() can return early without setting _isSetup when OverlayImage is null.
            // Checking _isSetup here prevents _StartOverlay from scheduling ticks that would
            // then crash trying to access OverlayImage.rectTransform on a null reference.
            if (_isSetup)
                _StartOverlay();
        }

        protected override void OnTrackingDeserialization()
        {
            if (!IsProcessRunning()) return;
            if (!_isSetup)
                Setup(TextureWidth, TextureHeight, WorldOrigin, PixelsPerUnitX, PixelsPerUnitZ);
            // Same null-safety check as OnTrackingStarted.
            if (_isSetup && !_isOverlayUpdating)
                _StartOverlay();
        }

        protected override void OnTrackingStopped(string[] playerIds)
        {
            _StopOverlay();
            _ClearAndFlush();
        }

        protected override void OnTrackingCompleted(string[] playerIds)
        {
            _StopOverlay();
            _ClearAndFlush();
        }

        /// <summary>
        /// Refreshes the overlay texture for the local player.
        /// Fires every <see cref="LocalUpdateInterval"/> seconds. Do not call directly.
        /// </summary>
        public void _OnLocalTick()
        {
            _scheduledLocalTickCount--;
            if (_scheduledLocalTickCount > 0 || !_isOverlayUpdating) return;

            _RecacheFields();

            int count = _ResolveTrackedPlayers();
            _DecayBuffer();
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi player = _playerBuffer[i];
                if (player != null && player.IsValid()) _DrawPlayer(player);
            }

            _FlushTexture();
            _ScheduleNextLocalTick();
        }

        /// <summary>
        /// Entry point of each remote-player refresh cycle.
        /// Scheduled every <see cref="RemoteUpdateInterval"/> seconds. Do not call directly.
        /// </summary>
        public void _OnRemoteTick()
        {
            _scheduledRemoteTickCount--;
            if (_scheduledRemoteTickCount > 0 || !_isOverlayUpdating) return;

            _RecacheFields();

            int count = _ResolveTrackedPlayers();
            _DecayBuffer();
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi player = _playerBuffer[i];
                if (player != null && player.IsValid()) _DrawPlayer(player);
            }

            _FlushTexture();
            _ScheduleNextRemoteTick();
        }


        private void _RecacheFields()
        {
            _overlayRectSize = OverlayImage.rectTransform.rect.size;
            _localColor32 = LocalPlayerColor;
            _remoteColor32 = RemotePlayerColor;
            _pixelsPerUnitX = PixelsPerUnitX;
            _pixelsPerUnitZ = PixelsPerUnitZ;
            _localMarkerRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(LocalMarkerWidth, LocalMarkerHeight) / 2f));
            _remoteMarkerRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(RemoteMarkerWidth, RemoteMarkerHeight) / 2f));

            // The decay factor is chosen so that after FadeMultiplier ticks a trail pixel drops
            // from full brightness (255) to roughly 8, which is visually indistinguishable from black.
            // Using integer math here keeps the per-pixel decay loop free of float operations.
            float d = Mathf.Pow(8f / 255f, 1f / Mathf.Max(1f, FadeMultiplier));
            _decayFactor = (byte)Mathf.Clamp(Mathf.RoundToInt(d * 256f), 0, 255);
        }

        /// <summary>
        /// Dims all existing pixels in the buffer by the precomputed decay factor.
        /// This replaces a hard clear, so old marker positions fade gradually rather than
        /// disappearing instantly. Players who have not moved are redrawn at full brightness
        /// on the same tick, so only trails from previous positions actually fade.
        /// Background pixels (alpha == 0) are skipped to keep the loop fast.
        /// </summary>
        private void _DecayBuffer()
        {
            byte df = _decayFactor;
            for (int i = 0; i < _pixelBuffer.Length; i++)
            {
                Color32 c = _pixelBuffer[i];
                if (c.a == 0) continue; // most pixels are background, skip them
                _pixelBuffer[i] = new Color32(
                    (byte)((c.r * df) >> 8),
                    (byte)((c.g * df) >> 8),
                    (byte)((c.b * df) >> 8),
                    (byte)((c.a * df) >> 8));
            }
        }

        private void _DrawPlayer(VRCPlayerApi player)
        {
            Vector3 pos = player.GetPosition();
            int px = Mathf.Clamp(
                Mathf.RoundToInt((pos.x - _worldOrigin.x) * _pixelsPerUnitX),
                0, _textureWidth - 1);
            int py = Mathf.Clamp(
                Mathf.RoundToInt((pos.z - _worldOrigin.z) * _pixelsPerUnitZ),
                0, _textureHeight - 1);

            bool isLocal = player.isLocal;
            Color32 color = isLocal ? _localColor32 : _remoteColor32;
            MarkerShape shape = isLocal ? LocalPlayerShape : RemotePlayerShape;

            if (shape == MarkerShape.Triangle)
            {
                float heading = player.GetRotation().eulerAngles.y;
                int halfW = isLocal
                    ? Mathf.Max(1, Mathf.RoundToInt(LocalMarkerWidth / 2f))
                    : Mathf.Max(1, Mathf.RoundToInt(RemoteMarkerWidth / 2f));
                int halfH = isLocal
                    ? Mathf.Max(1, Mathf.RoundToInt(LocalMarkerHeight / 2f))
                    : Mathf.Max(1, Mathf.RoundToInt(RemoteMarkerHeight / 2f));
                TextureGraphics2D.DrawTriangleToBuffer(
                    _pixelBuffer, _textureWidth, _textureHeight, px, py, halfW, halfH, heading, color);
            }
            else
            {
                int radius = isLocal ? _localMarkerRadius : _remoteMarkerRadius;
                TextureGraphics2D.DrawCircleToBuffer(
                    _pixelBuffer, _textureWidth, _textureHeight, px, py, radius, color);
            }
        }

        private void _FlushTexture()
        {
            if (_overlayTexture != null)
            {
                _overlayTexture.SetPixels32(_pixelBuffer);
                _overlayTexture.Apply();
            }
            TsEmit(OnOverlayUpdatedEvent);
        }

        private void _StartOverlay()
        {
            if (_isOverlayUpdating) return;
            _isOverlayUpdating = true;
            _ScheduleNextLocalTick();
            _ScheduleNextRemoteTick();
        }

        private void _StopOverlay()
        {
            _isOverlayUpdating = false;
        }

        private void _ScheduleNextLocalTick()
        {
            _scheduledLocalTickCount++;
            SendCustomEventDelayedSeconds(nameof(_OnLocalTick), LocalUpdateInterval);
        }

        private void _ScheduleNextRemoteTick()
        {
            _scheduledRemoteTickCount++;
            SendCustomEventDelayedSeconds(nameof(_OnRemoteTick), RemoteUpdateInterval);
        }

        /// <summary>
        /// Looks up live <see cref="VRCPlayerApi"/> references for each ID in
        /// <see cref="PlayerTracker.LastPlayerIds"/> and writes them into <see cref="_playerBuffer"/>.
        /// Returns the number of valid players found. No allocations.
        /// </summary>
        private int _ResolveTrackedPlayers()
        {
            string[] ids = LastPlayerIds;
            int count = 0;
            for (int i = 0; i < ids.Length && count < _playerBuffer.Length; i++)
            {
                VRCPlayerApi p = TsPlayer.FindPlayerByID(ids[i]);
                if (p == null || !p.IsValid()) continue;
                _playerBuffer[count++] = p;
            }
            return count;
        }

        private void _ClearAndFlush()
        {
            if (_overlayTexture == null || _pixelBuffer == null) return;
            System.Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
            _overlayTexture.SetPixels32(_pixelBuffer);
            _overlayTexture.Apply();
        }

    }
}
