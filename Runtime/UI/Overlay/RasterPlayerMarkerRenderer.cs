using Tsvrc.UI.Utils;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace Tsvrc.UI
{
    /// <summary>Shape vocabulary for <see cref="TextureGraphics2D"/>'s draw primitives.</summary>
    public enum MarkerShape
    {
        /// <summary>Filled circle. Size is controlled by the caller.</summary>
        Circle = 0,
        /// <summary>Filled triangle that can rotate to match a heading.</summary>
        Triangle = 1,
    }

    /// <summary>
    /// Optional <see cref="PlayerMarkerRenderer"/> base for painting markers into a raster
    /// texture. Owns the texture/pixel-buffer plumbing, world-to-pixel projection, and
    /// flush-only-when-dirty optimization; override <see cref="DrawMarker"/> to decide what gets
    /// painted.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RasterPlayerMarkerRenderer : PlayerMarkerRenderer
    {
        [Header("Display")]
        [Tooltip("RawImage that receives the generated overlay texture.")]
        public RawImage OverlayImage;

        [Tooltip(
            "Filter mode for the generated overlay texture.\n" +
            "Point = crisp pixel art, best for minimaps.\n" +
            "Bilinear / Trilinear = smooth when the RawImage is scaled larger than the texture.")]
        public FilterMode OverlayFilterMode = FilterMode.Point;

        private Texture2D _overlayTexture;
        private Color32[] _pixelBuffer;
        private int _textureWidth;
        private int _textureHeight;
        private Vector3 _worldOrigin;
        private float _unitsPerGridX;
        private float _unitsPerGridZ;
        private bool _isConfigured;

        // True iff the CPU buffer and GPU texture are both already blank - skips the clear+
        // upload once the overlay has nothing left to show.
        private bool _bufferIsClean = true;

        // Set by DrawMarker since the last OnPresent, so exactly one flush happens per cycle
        // no matter how many markers were drawn.
        private bool _dirtySinceLastPresent;

        /// <summary>Configures the overlay texture size and world-to-pixel mapping. Safe to call again to reconfigure.</summary>
        /// <param name="width">Overlay texture width in pixels.</param>
        /// <param name="height">Overlay texture height in pixels.</param>
        /// <param name="worldOrigin">World-space position mapping to pixel (0, 0), the bottom-left corner on the XZ plane.</param>
        /// <param name="unitsPerGridX">Pixels per world unit along X.</param>
        /// <param name="unitsPerGridZ">Pixels per world unit along Z.</param>
        public void Setup(int width, int height, Vector3 worldOrigin, float unitsPerGridX, float unitsPerGridZ)
        {
            if (OverlayImage == null)
            {
                LogError("OverlayImage is not assigned.");
                return;
            }

            if (width <= 0 || height <= 0)
            {
                LogError("Texture dimensions must be positive.");
                return;
            }

            if (unitsPerGridX <= 0f || unitsPerGridZ <= 0f)
            {
                LogError("UnitsPerGrid values must be positive.");
                return;
            }

            _worldOrigin = worldOrigin;
            _unitsPerGridX = unitsPerGridX;
            _unitsPerGridZ = unitsPerGridZ;

            if (_overlayTexture != null)
                Destroy(_overlayTexture);

            _overlayTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            _overlayTexture.filterMode = OverlayFilterMode;

            // Color32 zero-initialises to (0,0,0,0), fully transparent, so no explicit fill needed.
            _pixelBuffer = new Color32[width * height];
            _bufferIsClean = true;
            _dirtySinceLastPresent = false;
            TextureGraphics2D.FlushBuffer(_overlayTexture, _pixelBuffer);

            OverlayImage.texture = _overlayTexture;
            OverlayImage.color = Color.white;

            _textureWidth = width;
            _textureHeight = height;
            _isConfigured = true;
        }

        public override void OnMarkerVisible(Vector3 worldPosition, bool isLocalPlayer, float headingDegrees, string playerId)
        {
            if (!_isConfigured) return;

            int pixelX = Mathf.Clamp(
                Mathf.RoundToInt((worldPosition.x - _worldOrigin.x) * _unitsPerGridX),
                0, _textureWidth - 1);
            int pixelY = Mathf.Clamp(
                Mathf.RoundToInt((worldPosition.z - _worldOrigin.z) * _unitsPerGridZ),
                0, _textureHeight - 1);

            DrawMarker(_pixelBuffer, _textureWidth, _textureHeight, pixelX, pixelY, isLocalPlayer, headingDegrees, playerId);
            _dirtySinceLastPresent = true;
        }

        public override void OnPresent()
        {
            if (!_isConfigured) return;

            if (_dirtySinceLastPresent)
            {
                _dirtySinceLastPresent = false;
                _bufferIsClean = false;
                TextureGraphics2D.FlushBuffer(_overlayTexture, _pixelBuffer);
                return;
            }

            if (_bufferIsClean) return;
            TextureGraphics2D.ClearBuffer(_pixelBuffer);
            _bufferIsClean = true;
            TextureGraphics2D.FlushBuffer(_overlayTexture, _pixelBuffer);
        }

        /// <summary>Override to draw this player's marker into <paramref name="pixelBuffer"/> at the projected pixel coordinates.</summary>
        public virtual void DrawMarker(Color32[] pixelBuffer, int textureWidth, int textureHeight,
            int pixelX, int pixelY, bool isLocalPlayer, float headingDegrees, string playerId)
        {
        }
    }
}
