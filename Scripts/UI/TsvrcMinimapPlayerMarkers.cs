using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Tsvrc.UI
{
    /// <summary>
    /// Draws markers for all instance players on a minimap overlay texture.
    /// Uses a pre-allocated Color32 buffer with batched SetPixels32 for optimal performance.
    /// Compatible with any minimap that uses world-to-pixel coordinate mapping.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TsvrcMinimapPlayerMarkers : UdonSharpBehaviour
    {
        [Header("Display")]
        [Tooltip("RawImage overlay placed on top of the minimap display.")]
        public RawImage OverlayImage;

        [Header("Marker Appearance")]
        public Color LocalPlayerColor = Color.yellow;
        public Color RemotePlayerColor = Color.red;
        public int MarkerRadius = 6;

        [Header("Performance")]
        [Tooltip("Seconds between marker position updates.")]
        public float UpdateInterval = 0.3f;

        // Texture state
        private Texture2D _overlayTexture;
        private int _textureWidth;
        private int _textureHeight;

        // Coordinate mapping (world XZ -> texture pixels)
        private Vector3 _worldOrigin;
        private float _pixelsPerWorldUnitX;
        private float _pixelsPerWorldUnitZ;

        // Pre-allocated buffers
        private Color32[] _pixelBuffer;
        private VRCPlayerApi[] _playerBuffer;

        // Cached color conversions
        private Color32 _localColor32;
        private Color32 _remoteColor32;

        // Lifecycle flags
        private bool _initialized;
        private bool _updateRunning;

        /// <summary>
        /// Sets up the overlay texture and coordinate mapping, then starts the update loop.
        /// Parameters match TsvrcMarkerMapOverlay.Initialize for consistency.
        /// </summary>
        /// <param name="width">Overlay texture width in pixels (should match minimap texture).</param>
        /// <param name="height">Overlay texture height in pixels (should match minimap texture).</param>
        /// <param name="mapWorldOrigin">Bottom-left corner of the map area in world space (XZ plane).</param>
        /// <param name="worldToPixelScaleX">Pixels per world unit along X axis.</param>
        /// <param name="worldToPixelScaleZ">Pixels per world unit along Z axis.</param>
        public void Initialize(int width, int height, Vector3 mapWorldOrigin, float worldToPixelScaleX, float worldToPixelScaleZ)
        {
            if (OverlayImage == null)
            {
                Debug.LogError("[TsvrcMinimapPlayerMarkers] OverlayImage is not assigned.");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                Debug.LogError("[TsvrcMinimapPlayerMarkers] Invalid texture dimensions.");
                return;
            }

            if (worldToPixelScaleX <= 0f || worldToPixelScaleZ <= 0f)
            {
                Debug.LogError("[TsvrcMinimapPlayerMarkers] Invalid world-to-pixel scale.");
                return;
            }

            _textureWidth = width;
            _textureHeight = height;
            _worldOrigin = mapWorldOrigin;
            _pixelsPerWorldUnitX = worldToPixelScaleX;
            _pixelsPerWorldUnitZ = worldToPixelScaleZ;

            // Cache Color32 conversions to avoid per-frame conversion
            _localColor32 = LocalPlayerColor;
            _remoteColor32 = RemotePlayerColor;

            // Create overlay texture (RGBA32 for transparency support)
            _overlayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _overlayTexture.filterMode = FilterMode.Point;

            // Pre-allocate pixel buffer for batched writes
            int totalPixels = width * height;
            _pixelBuffer = new Color32[totalPixels];
            System.Array.Clear(_pixelBuffer, 0, totalPixels);
            _overlayTexture.SetPixels32(_pixelBuffer);
            _overlayTexture.Apply();

            OverlayImage.texture = _overlayTexture;
            OverlayImage.color = Color.white;

            // Pre-allocate player buffer (VRChat max is 82 players)
            if (_playerBuffer == null)
                _playerBuffer = new VRCPlayerApi[82];

            _initialized = true;

            // Start the update loop if not already running
            if (!_updateRunning)
            {
                _updateRunning = true;
                SendCustomEventDelayedSeconds(nameof(_UpdateLoop), UpdateInterval);
            }

            Debug.Log("[TsvrcMinimapPlayerMarkers] Initialized.");
        }

        /// <summary>
        /// Periodic update loop. Called via SendCustomEventDelayedSeconds.
        /// Do not call directly. Use Initialize() to start.
        /// </summary>
        public void _UpdateLoop()
        {
            if (!_initialized)
            {
                _updateRunning = false;
                return;
            }

            _DrawAllMarkers();
            SendCustomEventDelayedSeconds(nameof(_UpdateLoop), UpdateInterval);
        }

        /// <summary>
        /// Stops the update loop and clears all markers from the overlay.
        /// </summary>
        public void Clear()
        {
            _initialized = false;

            if (_overlayTexture != null && _pixelBuffer != null)
            {
                System.Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
                _overlayTexture.SetPixels32(_pixelBuffer);
                _overlayTexture.Apply();
            }

            Debug.Log("[TsvrcMinimapPlayerMarkers] Cleared.");
        }

        /// <summary>
        /// Gathers all player positions, converts to pixel coordinates, and draws markers
        /// using a single batched pixel buffer write for optimal performance.
        /// </summary>
        private void _DrawAllMarkers()
        {
            if (_overlayTexture == null || _pixelBuffer == null) return;

            // Clear pixel buffer to transparent
            System.Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);

            // Get all players in the instance
            int playerCount = VRCPlayerApi.GetPlayerCount();
            if (playerCount <= 0) return;

            // Resize buffer if needed (shouldn't happen with 82 max, but safe)
            if (_playerBuffer.Length < playerCount)
                _playerBuffer = new VRCPlayerApi[playerCount];

            VRCPlayerApi.GetPlayers(_playerBuffer);

            // Draw a marker for each valid player
            for (int i = 0; i < playerCount; i++)
            {
                VRCPlayerApi player = _playerBuffer[i];
                if (player == null || !player.IsValid()) continue;

                Vector3 worldPos = player.GetPosition();

                // World position -> pixel coordinates
                int pixelX = Mathf.RoundToInt((worldPos.x - _worldOrigin.x) * _pixelsPerWorldUnitX);
                int pixelY = Mathf.RoundToInt((worldPos.z - _worldOrigin.z) * _pixelsPerWorldUnitZ);

                // Clamp to texture bounds
                pixelX = Mathf.Clamp(pixelX, 0, _textureWidth - 1);
                pixelY = Mathf.Clamp(pixelY, 0, _textureHeight - 1);

                // Select color based on local vs remote
                Color32 color = player.isLocal ? _localColor32 : _remoteColor32;

                // Draw filled circle directly into pixel buffer
                _DrawCircleToBuffer(pixelX, pixelY, MarkerRadius, color);
            }

            // Single batched write to GPU
            _overlayTexture.SetPixels32(_pixelBuffer);
            _overlayTexture.Apply();
        }

        /// <summary>
        /// Draws a filled circle into the pre-allocated pixel buffer.
        /// Uses direct array indexing instead of per-pixel SetPixel calls for performance.
        /// </summary>
        private void _DrawCircleToBuffer(int cx, int cy, int radius, Color32 color)
        {
            int radiusSq = radius * radius;

            for (int dy = -radius; dy <= radius; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= _textureHeight) continue;

                int rowOffset = py * _textureWidth;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radiusSq) continue;

                    int px = cx + dx;
                    if (px < 0 || px >= _textureWidth) continue;

                    _pixelBuffer[rowOffset + px] = color;
                }
            }
        }
    }
}
