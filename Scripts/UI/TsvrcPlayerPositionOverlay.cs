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
        /// <summary>Filled circle. Radius = min(MarkerWidth, MarkerHeight) / 2.</summary>
        Circle = 0,
        /// <summary>Filled triangle pointing in the player's facing direction (heading-aware).</summary>
        Triangle = 1,
    }

    /// <summary>
    /// Projects a set of tracked VRChat player world-positions onto a 2D <see cref="RawImage"/>
    /// overlay texture. Extends <see cref="PlayerTracker"/> — the owner controls which players
    /// are displayed and all clients receive network events when the set changes.
    ///
    /// Every client runs its own local draw loop; no rendering state is synced.
    ///
    /// Lifecycle:
    ///   1. (optional) <see cref="Setup"/> — override texture dimensions and world-to-pixel mapping at runtime.
    ///                                       If not called, inspector defaults are used automatically.
    ///   2. <see cref="StartOverlay"/>     — owner starts the process; all clients begin drawing.
    ///   3. <see cref="PlayerTracker.AddTrackedPlayers"/> /
    ///      <see cref="PlayerTracker.RemoveTrackedPlayers"/> — owner dynamically changes the displayed set.
    ///   4. <see cref="StopOverlay"/>      — owner ends the process; all clients clear the overlay.
    ///
    /// Subscribe to <see cref="OnOverlayUpdatedEvent"/> via <c>TsSubscribe</c> for per-cycle callbacks.
    ///
    /// All players are rendered into the overlay texture every <see cref="RemoteUpdateInterval"/> seconds.
    /// Local and remote players can use independent <see cref="MarkerShape"/> types and colors.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcPlayerPositionOverlay : PlayerTracker
    {
        /// <summary>
        /// Emitted via <see cref="TsvrcBehaviour.TsEmit"/> after every complete remote-player
        /// render cycle. Read the overlay texture from <see cref="OverlayImage"/> in your callback.
        /// </summary>
        public const string OnOverlayUpdatedEvent = "OnOverlayUpdated";

        // ------------------------------------------------------------------
        // Inspector
        // ------------------------------------------------------------------

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
        [Tooltip("Seconds between full texture refreshes for the local player. Lower values make your own marker more responsive.")]
        [Min(0.05f)] public float LocalUpdateInterval = 0.1f;

        [Tooltip("Seconds between full texture refreshes for remote players.")]
        [Min(0.05f)] public float RemoteUpdateInterval = 0.3f;

        // ------------------------------------------------------------------
        // Private state
        // ------------------------------------------------------------------

        private Texture2D      _overlayTexture;
        private int            _textureWidth;
        private int            _textureHeight;
        private Vector3        _worldOrigin;
        private float          _pixelsPerUnitX;
        private float          _pixelsPerUnitZ;
        // Derived from min(Local/RemoteMarkerWidth, Local/RemoteMarkerHeight) / 2; recached each
        // tick so runtime inspector changes take effect without requiring a new Setup() call.
        private int            _localMarkerRadius;
        private int            _remoteMarkerRadius;

        // Pre-allocated, GC-free buffers
        private Color32[]      _pixelBuffer;
        private VRCPlayerApi[] _playerBuffer;   // VRChat instance cap is 82 players

        // Cached per tick; recached so runtime color changes take effect immediately
        private Color32        _localColor32;
        private Color32        _remoteColor32;
        private Vector2        _overlayRectSize;

        // Lifecycle flags
        private bool           _isSetup;
        private bool           _isOverlayUpdating;

        // Pending event counters — double-tick guard per loop.
        // Incremented when a tick is scheduled, decremented when it fires.
        // If > 0 when a tick fires, a newer tick is already queued; the stale one discards itself.
        private int            _scheduledLocalTickCount;
        private int            _scheduledRemoteTickCount;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Configures the overlay texture dimensions, world-to-pixel mapping, and scale.
        /// Call on every client independently, before or after <see cref="StartOverlay"/>.
        /// If the process is already running on this client the draw loop starts immediately.
        /// Safe to call again while running — the texture is recreated and the next tick uses
        /// the new configuration.
        /// </summary>
        /// <param name="width">Overlay texture width in pixels.</param>
        /// <param name="height">Overlay texture height in pixels.</param>
        /// <param name="worldOrigin">World-space bottom-left corner of the mapped area (XZ plane). Pixel (0,0) maps here.</param>
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

            _textureWidth   = width;
            _textureHeight  = height;
            _worldOrigin    = worldOrigin;
            // Mirror to inspector fields so the inspector reflects the active configuration
            // and future auto-setup paths read the correct defaults on subsequent calls.
            PixelsPerUnitX  = pixelsPerUnitX;
            PixelsPerUnitZ  = pixelsPerUnitZ;
            _pixelsPerUnitX = pixelsPerUnitX;
            _pixelsPerUnitZ = pixelsPerUnitZ;
            _localMarkerRadius  = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(LocalMarkerWidth,  LocalMarkerHeight)  / 2f));
            _remoteMarkerRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(RemoteMarkerWidth, RemoteMarkerHeight) / 2f));
            _localColor32   = LocalPlayerColor;
            _remoteColor32  = RemotePlayerColor;

            if (_overlayTexture != null)
                Destroy(_overlayTexture);

            _overlayTexture            = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _overlayTexture.filterMode = OverlayFilterMode;

            // default(Color32) == (0,0,0,0) == transparent — no explicit fill needed
            _pixelBuffer = new Color32[width * height];
            _overlayTexture.SetPixels32(_pixelBuffer);
            _overlayTexture.Apply();

            OverlayImage.texture = _overlayTexture;
            OverlayImage.color   = Color.white;

            if (_playerBuffer == null)
                _playerBuffer = new VRCPlayerApi[82];

            _overlayRectSize = OverlayImage.rectTransform.rect.size;

            _isSetup = true;

            if (IsProcessRunning())
                _StartOverlay();
        }

        /// <summary>
        /// Single-call entry point for the overlay owner.
        /// Uses inspector defaults (<see cref="TextureWidth"/>, <see cref="TextureHeight"/>,
        /// <see cref="WorldOrigin"/>, <see cref="PixelsPerUnitX"/>, <see cref="PixelsPerUnitZ"/>)
        /// if <see cref="Setup"/> was not called first; otherwise preserves the explicitly configured values.
        /// </summary>
        public void StartOverlay(string[] playerIds)
        {
            if (!_isSetup)
                Setup(TextureWidth, TextureHeight, WorldOrigin, PixelsPerUnitX, PixelsPerUnitZ);
            StartPlayerTracking(playerIds);
        }

        /// <summary>
        /// Stops the overlay and clears the texture on all clients.
        /// Symmetric counterpart to <see cref="StartOverlay"/>.
        /// </summary>
        public void StopOverlay()
        {
            StopPlayerTracking();
        }

        // ------------------------------------------------------------------
        // PlayerTracker overrides
        // ------------------------------------------------------------------

        protected override void OnTrackingStarted(string[] playerIds)
        {
            if (!_isSetup)
                Setup(TextureWidth, TextureHeight, WorldOrigin, PixelsPerUnitX, PixelsPerUnitZ);
            // Guard: Setup() may return without setting _isSetup when OverlayImage is null.
            // Without this, _StartOverlay() would schedule _OnRemoteTick which then
            // dereferences OverlayImage.rectTransform and throws a NullReferenceException.
            if (_isSetup)
                _StartOverlay();
        }

        protected override void OnTrackingDeserialization()
        {
            if (!IsProcessRunning()) return;
            if (!_isSetup)
                Setup(TextureWidth, TextureHeight, WorldOrigin, PixelsPerUnitX, PixelsPerUnitZ);
            // Same _isSetup guard as OnTrackingStarted — Setup() may silently fail.
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

        // ------------------------------------------------------------------
        // Remote tick — pixel-buffer draw loop (all circle markers)
        // ------------------------------------------------------------------

        /// <summary>
        /// Entry point of each local-player refresh cycle.
        /// Scheduled every <see cref="LocalUpdateInterval"/> seconds. Do not call directly.
        /// </summary>
        public void _OnLocalTick()
        {
            _scheduledLocalTickCount--;
            if (_scheduledLocalTickCount > 0 || !_isOverlayUpdating) return;

            _RecacheFields();

            int count = _ResolveTrackedPlayers();
            System.Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
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
            System.Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi player = _playerBuffer[i];
                if (player != null && player.IsValid()) _DrawPlayer(player);
            }

            _FlushTexture();
            _ScheduleNextRemoteTick();
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private void _RecacheFields()
        {
            _overlayRectSize    = OverlayImage.rectTransform.rect.size;
            _localColor32       = LocalPlayerColor;
            _remoteColor32      = RemotePlayerColor;
            _pixelsPerUnitX     = PixelsPerUnitX;
            _pixelsPerUnitZ     = PixelsPerUnitZ;
            _localMarkerRadius  = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(LocalMarkerWidth,  LocalMarkerHeight)  / 2f));
            _remoteMarkerRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(RemoteMarkerWidth, RemoteMarkerHeight) / 2f));
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

            bool isLocal      = player.isLocal;
            Color32 color     = isLocal ? _localColor32 : _remoteColor32;
            MarkerShape shape = isLocal ? LocalPlayerShape : RemotePlayerShape;

            if (shape == MarkerShape.Triangle)
            {
                float heading = player.GetRotation().eulerAngles.y;
                int halfW = isLocal
                    ? Mathf.Max(1, Mathf.RoundToInt(LocalMarkerWidth  / 2f))
                    : Mathf.Max(1, Mathf.RoundToInt(RemoteMarkerWidth / 2f));
                int halfH = isLocal
                    ? Mathf.Max(1, Mathf.RoundToInt(LocalMarkerHeight  / 2f))
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
        /// Resolves <see cref="PlayerTracker.LastPlayerIds"/> into live <see cref="VRCPlayerApi"/>
        /// refs stored in <see cref="_playerBuffer"/>. GC-free — no array allocations.
        /// </summary>
        private int _ResolveTrackedPlayers()
        {
            string[] ids = LastPlayerIds;
            int count    = 0;
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
