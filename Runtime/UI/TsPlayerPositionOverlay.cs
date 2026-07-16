using Tsvrc.Tracking;
using Tsvrc.UI.Utils;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Tsvrc.UI
{
    /// <summary>
    /// Shape used to draw a player marker in the <see cref="TsPlayerPositionOverlay"/> texture.
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
    /// <b>Tick architecture</b>
    /// <list type="bullet">
    ///   <item><b>Remote tick</b> (<see cref="RemoteUpdateInterval"/>): resolves all tracked
    ///         players and caches their pixel positions. Does not draw or flush.</item>
    ///   <item><b>Blink tick</b> (<see cref="MarkerOnDuration"/>, <see cref="MarkerOffDuration"/>):
    ///         alternates between drawing markers from the position cache and clearing the texture,
    ///         creating a periodic appear/disappear cycle independent of position refresh rate.</item>
    /// </list>
    ///
    /// Typical usage:
    /// <list type="number">
    ///   <item>Call <see cref="Setup"/> from code to configure texture dimensions and world
    ///         mapping. Must be called before <see cref="StartOverlay"/>.</item>
    ///   <item>Call <see cref="StartOverlay"/> from the owner to begin tracking and drawing.</item>
    ///   <item>Use <see cref="PlayerTracker.AddTrackedPlayers"/> and
    ///         <see cref="PlayerTracker.RemoveTrackedPlayers"/> to change the displayed set at runtime.</item>
    ///   <item>Call <see cref="StopOverlay"/> to end the process and clear the overlay on all clients.</item>
    /// </list>
    ///
    /// Subscribe to <see cref="OnOverlayUpdatedEvent"/> via <c>TsSubscribe</c> to receive a
    /// callback after each draw cycle (including after a clear).
    ///
    /// Local and remote players each support an independent marker shape and colour.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsPlayerPositionOverlay : PlayerTracker
    {
        /// <summary>
        /// Emitted via <see cref="TsBehaviour.TsEmit"/> after every draw cycle completes,
        /// including when the overlay is cleared. Read <see cref="OverlayImage"/> in your callback.
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

        [Header("Local Player")]
        [Tooltip("Shape used to draw the local player marker in the overlay texture.")]
        public MarkerShape LocalPlayerShape = MarkerShape.Circle;
        public Color LocalPlayerColor = Color.yellow;
        [Tooltip("Local player marker width in texture pixels. Changes take effect on the next remote tick.")]
        [Min(1f)] public float LocalMarkerWidth = 12f;
        [Tooltip("Local player marker height in texture pixels. Changes take effect on the next remote tick.")]
        [Min(1f)] public float LocalMarkerHeight = 12f;

        [Header("Remote Players")]
        [Tooltip("Shape used to draw remote player markers in the overlay texture.")]
        public MarkerShape RemotePlayerShape = MarkerShape.Circle;
        public Color RemotePlayerColor = Color.red;
        [Tooltip("Remote player marker width in texture pixels. Changes take effect on the next remote tick.")]
        [Min(1f)] public float RemoteMarkerWidth = 12f;
        [Tooltip("Remote player marker height in texture pixels. Changes take effect on the next remote tick.")]
        [Min(1f)] public float RemoteMarkerHeight = 12f;

        [Header("Performance")]
        [Tooltip("How often remote player markers refresh, in seconds.")]
        [Min(0.05f)] public float RemoteUpdateInterval = 0.3f;

        [Header("Marker Timing")]
        [Tooltip("How long player markers stay visible before the overlay clears, in seconds.")]
        [Min(0.1f)] public float MarkerOnDuration = 2f;

        [Tooltip("How long the overlay stays blank before markers reappear, in seconds.")]
        [Min(0.1f)] public float MarkerOffDuration = 1f;

        private Texture2D _overlayTexture;
        private int _textureWidth;
        private int _textureHeight;
        private Vector3 _worldOrigin;
        private float _pixelsPerUnitX;
        private float _pixelsPerUnitZ;
        // Radii are derived from marker dimensions each remote tick so inspector edits
        // take effect without a new Setup() call.
        private int _localMarkerRadius;
        private int _remoteMarkerRadius;

        // Pre-allocated buffers, reused every tick to avoid GC pressure.
        // _playerBuffer    : tracked players resolved from LastPlayerIds (output of _ResolveTrackedPlayers).
        // _allPlayersBuffer: all instance players from VRCPlayerApi.GetPlayers() (input, no-alloc).
        private Color32[] _pixelBuffer;
        private VRCPlayerApi[] _playerBuffer;
        private VRCPlayerApi[] _allPlayersBuffer;

        // Recached at the start of each remote tick so inspector edits take effect
        // without requiring a new Setup() call. Blink tick uses the latest cached values.
        private Color32 _localColor32;
        private Color32 _remoteColor32;

        // Blink state, toggled by the blink tick.
        private bool _markersVisible;
        // True iff _pixelBuffer is all zeros AND the GPU texture already displays blank.
        // Guards two expensive operations in every hide cycle and in _ClearAndFlush:
        //   1. Array.Clear: CPU memset of the full pixel buffer
        //   2. SetPixels32 + Apply: full CPU to GPU texture upload
        // Source: Unity 2022.3 docs. "Apply is an expensive operation because it copies all
        // the pixels in the texture even if you've only changed some."
        // Set false only when pixels are actually drawn; set true only after clearing the
        // CPU buffer AND uploading the blank image to the GPU.
        private bool _bufferIsClean = true;

        // Per-frame position cache populated by the remote tick and consumed by the blink tick.
        // Stores the last-known pixel position of each tracked player so the blink tick can
        // draw remote markers without calling VRCPlayerApi.GetPlayers() again.
        private int[] _cachedPxArr;
        private int[] _cachedPyArr;
        private float[] _cachedHeadings;   // only used when MarkerShape == Triangle
        private bool[] _cachedIsLocalArr;
        private int _cachedPlayerCount;

        // Cached triangle half-sizes, populated by Setup() and refreshed each remote tick
        // via _RecacheFields(). Avoids Mathf.RoundToInt inside _DrawPlayerAtPixel per call.
        private int _localHalfW;
        private int _localHalfH;
        private int _remoteHalfW;
        private int _remoteHalfH;

        private bool _isSetup;
        private bool _isOverlayUpdating;

        // VRChat's current per-instance player cap. Sizes every fixed-capacity buffer below.
        private const int MaxTrackedPlayers = 82;

        // Double-tick guard: each schedule call increments the counter; each fired event
        // decrements it. If the counter is still > 0 when a tick fires a newer tick is already
        // queued so the current one is stale and is discarded, preventing duplicate draw cycles.
        private int _scheduledBlinkTickCount;
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
                Debug.LogError("[TsPlayerPositionOverlay] OverlayImage is not assigned.");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogError("[TsPlayerPositionOverlay] Texture dimensions must be positive.");
                return;
            }

            if (pixelsPerUnitX <= 0f || pixelsPerUnitZ <= 0f)
            {
                Debug.LogError("[TsPlayerPositionOverlay] PixelsPerUnit values must be positive.");
                return;
            }

            _textureWidth = width;
            _textureHeight = height;
            _worldOrigin = worldOrigin;
            _pixelsPerUnitX = pixelsPerUnitX;
            _pixelsPerUnitZ = pixelsPerUnitZ;
            _RecacheFields();

            if (_overlayTexture != null)
                Destroy(_overlayTexture);

            _overlayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _overlayTexture.filterMode = OverlayFilterMode;

            // Color32 zero-initialises to (0,0,0,0), fully transparent, so no explicit fill needed.
            _pixelBuffer = new Color32[width * height];
            _bufferIsClean = true;
            TextureGraphics2D.FlushBuffer(_overlayTexture, _pixelBuffer);

            OverlayImage.texture = _overlayTexture;
            OverlayImage.color = Color.white;

            if (_playerBuffer == null)
                _playerBuffer = new VRCPlayerApi[MaxTrackedPlayers];
            if (_allPlayersBuffer == null)
                _allPlayersBuffer = new VRCPlayerApi[MaxTrackedPlayers];

            if (_cachedPxArr == null)
            {
                _cachedPxArr = new int[MaxTrackedPlayers];
                _cachedPyArr = new int[MaxTrackedPlayers];
                _cachedHeadings = new float[MaxTrackedPlayers];
                _cachedIsLocalArr = new bool[MaxTrackedPlayers];
            }
            _cachedPlayerCount = 0;

            _isSetup = true;

            if (IsProcessRunning())
            {
                // Restart the running overlay so the new texture dimensions and world mapping
                // take effect immediately. _StopOverlay clears the running flag; the pending
                // blink/remote ticks will fire and skip (counter guard + !_isOverlayUpdating);
                // _StartOverlay then queues fresh ticks against the new buffers.
                _StopOverlay();
                _StartOverlay();
            }
        }

        /// <summary>
        /// Starts the overlay and begins tracking the given player IDs.
        /// <see cref="Setup"/> must be called from code before this.
        /// </summary>
        public void StartOverlay(string[] playerIds)
        {
            if (!_isSetup)
            {
                Debug.LogError("[TsPlayerPositionOverlay] Setup() must be called before StartOverlay().");
                return;
            }
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
            if (!_isSetup) return;
            _StartOverlay();
        }

        protected override void OnTrackingDeserialization()
        {
            if (!IsProcessRunning() || !_isSetup || _isOverlayUpdating) return;
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
        /// Blink tick. Alternates between drawing all tracked player markers and clearing the
        /// texture, creating a periodic appear/disappear cycle. When visible, remote positions
        /// are drawn from the integer cache (no <c>VRCPlayerApi.GetPlayers</c> call) and the
        /// local player is redrawn at its current world position (one <c>GetPosition</c> call).
        /// When hidden, the texture is cleared. The next tick is scheduled with
        /// <see cref="MarkerOnDuration"/> after a show or <see cref="MarkerOffDuration"/> after
        /// a hide. Do not call directly.
        /// </summary>
        public void _OnBlinkTick()
        {
            _scheduledBlinkTickCount--;
            if (_scheduledBlinkTickCount > 0 || !_isOverlayUpdating) return;

            _markersVisible = !_markersVisible;

            bool bufferModified = false;

            if (_markersVisible)
            {
                // Show cycle: the buffer is guaranteed clean from the preceding hide or Setup,
                // so no Array.Clear is needed here.

                // Recache colours so inspector changes are picked up on each show.
                _localColor32 = LocalPlayerColor;
                _remoteColor32 = RemotePlayerColor;

                // Draw remote players from the integer cache. No VRCPlayerApi calls needed.
                // Skip the local player slot since it is redrawn fresh below.
                bool localPlayerTracked = false;
                for (int i = 0; i < _cachedPlayerCount; i++)
                {
                    if (_cachedIsLocalArr[i])
                    {
                        localPlayerTracked = true;
                        continue;
                    }
                    _DrawPlayerAtPixel(
                        _cachedPxArr[i], _cachedPyArr[i],
                        false, _cachedHeadings[i], _remoteColor32);
                    bufferModified = true;
                }

                // Draw local player at its actual current position (one cheap API call).
                if (localPlayerTracked)
                {
                    VRCPlayerApi localPlayer = Networking.LocalPlayer;
                    if (localPlayer != null && localPlayer.IsValid())
                    {
                        _DrawPlayer(localPlayer);
                        bufferModified = true;
                    }
                }

                if (bufferModified)
                    _bufferIsClean = false;
            }
            else
            {
                // Hide cycle: remove drawn markers so the texture shows blank.
                // Guard prevents a wasted clear + GPU upload when no markers were drawn
                // in the preceding show cycle (e.g. no tracked players in the instance).
                if (!_bufferIsClean)
                {
                    TextureGraphics2D.ClearBuffer(_pixelBuffer);
                    _bufferIsClean = true;
                    bufferModified = true;
                }
            }

            // Always emit the event, even on a skipped flush, so subscribers are notified every cycle.
            if (bufferModified)
                _FlushTexture();
            else
                TsEmit(OnOverlayUpdatedEvent);

            _ScheduleNextBlinkTick();
        }

        /// <summary>
        /// Authoritative remote tick. Resolves all tracked players via a single
        /// <c>VRCPlayerApi.GetPlayers</c> call and caches their pixel positions and headings for
        /// the blink tick. Does not draw or flush; all drawing is handled by
        /// <see cref="_OnBlinkTick"/>. Fires every <see cref="RemoteUpdateInterval"/> seconds.
        /// Do not call directly.
        /// </summary>
        public void _OnRemoteTick()
        {
            _scheduledRemoteTickCount--;
            if (_scheduledRemoteTickCount > 0 || !_isOverlayUpdating) return;

            _RecacheFields();
            int count = _ResolveTrackedPlayers();

            // Populate the blink-tick position cache. No draw, no flush.
            // Cache stores only valid players (compacted) so the blink tick needs no null checks.
            int cacheCount = 0;
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi player = _playerBuffer[i];
                if (player == null || !player.IsValid()) continue;

                Vector3 pos = player.GetPosition();
                bool isLocal = player.isLocal;
                MarkerShape shape = isLocal ? LocalPlayerShape : RemotePlayerShape;
                // Only call GetRotation() for triangle markers. The quaternion-to-Euler
                // conversion is wasted for circles, which ignore the heading entirely.
                float heading = shape == MarkerShape.Triangle
                    ? player.GetRotation().eulerAngles.y : 0f;
                int px = Mathf.Clamp(
                    Mathf.RoundToInt((pos.x - _worldOrigin.x) * _pixelsPerUnitX),
                    0, _textureWidth - 1);
                int py = Mathf.Clamp(
                    Mathf.RoundToInt((pos.z - _worldOrigin.z) * _pixelsPerUnitZ),
                    0, _textureHeight - 1);

                _cachedPxArr[cacheCount] = px;
                _cachedPyArr[cacheCount] = py;
                _cachedHeadings[cacheCount] = heading;
                _cachedIsLocalArr[cacheCount] = isLocal;
                cacheCount++;
            }
            _cachedPlayerCount = cacheCount;

            _ScheduleNextRemoteTick();
        }


        private void _RecacheFields()
        {
            _localColor32 = LocalPlayerColor;
            _remoteColor32 = RemotePlayerColor;
            _localMarkerRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(LocalMarkerWidth, LocalMarkerHeight) / 2f));
            _remoteMarkerRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(RemoteMarkerWidth, RemoteMarkerHeight) / 2f));
            // Triangle half-sizes, cached so _DrawPlayerAtPixel skips per-call Mathf.RoundToInt.
            _localHalfW = Mathf.Max(1, Mathf.RoundToInt(LocalMarkerWidth / 2f));
            _localHalfH = Mathf.Max(1, Mathf.RoundToInt(LocalMarkerHeight / 2f));
            _remoteHalfW = Mathf.Max(1, Mathf.RoundToInt(RemoteMarkerWidth / 2f));
            _remoteHalfH = Mathf.Max(1, Mathf.RoundToInt(RemoteMarkerHeight / 2f));
        }

        // Thin wrapper: computes pixel position from world position then delegates to _DrawPlayerAtPixel.
        // Called by the blink tick for the fresh local-player draw (only place a VRCPlayerApi is
        // needed at draw time).
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
            MarkerShape shape = isLocal ? LocalPlayerShape : RemotePlayerShape;
            // Only call GetRotation() when the shape actually uses the heading.
            float heading = shape == MarkerShape.Triangle
                ? player.GetRotation().eulerAngles.y : 0f;
            _DrawPlayerAtPixel(px, py, isLocal, heading,
                isLocal ? _localColor32 : _remoteColor32);
        }

        // Core pixel-space draw. Accepts pre-computed coordinates so the blink tick can draw
        // remote players from cache without any VRCPlayerApi calls.
        private void _DrawPlayerAtPixel(int px, int py, bool isLocal, float heading, Color32 color)
        {
            MarkerShape shape = isLocal ? LocalPlayerShape : RemotePlayerShape;
            if (shape == MarkerShape.Triangle)
            {
                int halfW = isLocal ? _localHalfW : _remoteHalfW;
                int halfH = isLocal ? _localHalfH : _remoteHalfH;
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
                TextureGraphics2D.FlushBuffer(_overlayTexture, _pixelBuffer);
            TsEmit(OnOverlayUpdatedEvent);
        }

        private void _StartOverlay()
        {
            if (_isOverlayUpdating) return;
            _isOverlayUpdating = true;
            // Reset cache so blink ticks don't draw stale positions from a previous run.
            _cachedPlayerCount = 0;
            _markersVisible = false;
            // Delay the first blink slightly so the remote tick can populate the cache first.
            _scheduledBlinkTickCount++;
            SendCustomEventDelayedSeconds(nameof(_OnBlinkTick), RemoteUpdateInterval + 0.05f);
            _ScheduleNextRemoteTick();
        }

        private void _StopOverlay()
        {
            _isOverlayUpdating = false;
        }

        private void _ScheduleNextBlinkTick()
        {
            _scheduledBlinkTickCount++;
            // After a show, schedule the hide; after a hide, schedule the show.
            float delay = _markersVisible ? MarkerOnDuration : MarkerOffDuration;
            SendCustomEventDelayedSeconds(nameof(_OnBlinkTick), delay);
        }

        private void _ScheduleNextRemoteTick()
        {
            _scheduledRemoteTickCount++;
            SendCustomEventDelayedSeconds(nameof(_OnRemoteTick), RemoteUpdateInterval);
        }

        /// <summary>
        /// Resolves <see cref="PlayerTracker.LastPlayerIds"/> to live <see cref="VRCPlayerApi"/>
        /// references and writes them into <see cref="_playerBuffer"/>.
        /// Returns the number of valid players found.
        ///
        /// <b>Zero allocation</b>: calls <c>VRCPlayerApi.GetPlayers(_allPlayersBuffer)</c> once,
        /// then for each tracked ID parses the trailing <c>playerId</c> integer from the
        /// <c>"displayName#playerId"</c> string (zero alloc, character arithmetic) and matches
        /// it against <c>VRCPlayerApi.playerId</c> (an <c>int</c> field, no allocation).
        /// </summary>
        private int _ResolveTrackedPlayers()
        {
            string[] ids = LastPlayerIds;
            if (ids.Length == 0) return 0;

            // VRCPlayerApi.GetPlayers() writes in-place and pads with null beyond player count.
            int totalCount = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi.GetPlayers(_allPlayersBuffer);

            int count = 0;
            for (int i = 0; i < ids.Length && count < _playerBuffer.Length; i++)
            {
                int targetIntId = _ParsePlayerIntId(ids[i]);
                if (targetIntId < 0) continue; // malformed ID, skip
                for (int j = 0; j < totalCount; j++)
                {
                    VRCPlayerApi p = _allPlayersBuffer[j];
                    if (p == null || !p.IsValid()) continue;
                    if (p.playerId == targetIntId)
                    {
                        _playerBuffer[count++] = p;
                        break;
                    }
                }
            }
            return count;
        }

        // Parses the trailing int after the last '#' in a "displayName#playerId" string
        // without allocating. Returns -1 for a malformed or missing suffix.
        private int _ParsePlayerIntId(string id)
        {
            int hashPos = -1;
            for (int i = id.Length - 1; i >= 0; i--)
            {
                if (id[i] == '#') { hashPos = i; break; }
            }
            if (hashPos < 0 || hashPos == id.Length - 1) return -1;
            int result = 0;
            for (int i = hashPos + 1; i < id.Length; i++)
            {
                char c = id[i];
                if (c < '0' || c > '9') return -1;
                result = result * 10 + (c - '0');
            }
            return result;
        }

        private void _ClearAndFlush()
        {
            if (_overlayTexture == null || _pixelBuffer == null) return;
            // Only upload to GPU if the buffer contained drawn markers to clear.
            // If _bufferIsClean is already true the GPU texture is already blank,
            // so SetPixels32 + Apply would copy an unchanged buffer for no effect.
            if (!_bufferIsClean)
            {
                TextureGraphics2D.ClearBuffer(_pixelBuffer);
                _bufferIsClean = true;
                TextureGraphics2D.FlushBuffer(_overlayTexture, _pixelBuffer);
            }
            // Always notify subscribers regardless of whether the GPU was updated.
            TsEmit(OnOverlayUpdatedEvent);
        }

    }
}
