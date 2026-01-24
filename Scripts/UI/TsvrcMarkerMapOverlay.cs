using Tsvrc.Core;
using Tsvrc.UI.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Tsvrc.UI
{
    public class TsvrcMarkerMapOverlay : TsvrcBehaviour
    {
        public RawImage OverlayImage;
        public Color PlayerColor = Color.yellow;
        public int MarkerRadius = 6;
        [Tooltip("How often to redraw the marker in seconds (e.g., 0.5 for twice per second, 1.0 for once per second)")]
        public float UpdateInterval = 0.5f;

        private Texture2D overlayTexture;
        private int textureWidth;
        private int textureHeight;
        private int lastPixelX = -1;
        private int lastPixelY = -1;
        private bool updateScheduled = false;
        private Vector3 pendingWorldPos;

        // Coordinate conversion parameters
        private Vector3 worldOrigin;
        private float pixelsPerWorldUnitX;
        private float pixelsPerWorldUnitZ;

        public void Initialize(int width, int height, Vector3 mapWorldOrigin, float worldToPixelScaleX, float worldToPixelScaleZ)
        {
            if (OverlayImage == null) return;

            textureWidth = width;
            textureHeight = height;
            worldOrigin = mapWorldOrigin;
            pixelsPerWorldUnitX = worldToPixelScaleX;
            pixelsPerWorldUnitZ = worldToPixelScaleZ;

            // Create transparent overlay texture
            overlayTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
            TextureGraphics2D.ClearTexture(overlayTexture);

            OverlayImage.texture = overlayTexture;
        }

        public void UpdatePositionFromWorld(Vector3 worldPos)
        {
            if (overlayTexture == null || pixelsPerWorldUnitX <= 0 || pixelsPerWorldUnitZ <= 0) return;

            // Store the pending position
            pendingWorldPos = worldPos;

            // Schedule an update if one isn't already scheduled
            if (!updateScheduled)
            {
                updateScheduled = true;
                SendCustomEventDelayedSeconds(nameof(_DelayedUpdate), UpdateInterval);
            }
        }

        public void _DelayedUpdate()
        {
            updateScheduled = false;

            if (overlayTexture == null || pixelsPerWorldUnitX <= 0 || pixelsPerWorldUnitZ <= 0) return;

            // Convert world position to pixel coordinates
            int pixelX, pixelY;
            WorldToPixel(pendingWorldPos, out pixelX, out pixelY);

            UpdatePosition(pixelX, pixelY);
        }

        private void UpdatePosition(int pixelX, int pixelY)
        {
            if (overlayTexture == null) return;

            // Only redraw if position changed
            if (pixelX == lastPixelX && pixelY == lastPixelY) return;

            // Clamp to bounds
            pixelX = Mathf.Clamp(pixelX, 0, textureWidth - 1);
            pixelY = Mathf.Clamp(pixelY, 0, textureHeight - 1);

            lastPixelX = pixelX;
            lastPixelY = pixelY;

            // Clear and redraw marker
            TextureGraphics2D.ClearTexture(overlayTexture);
            TextureGraphics2D.DrawCircle(overlayTexture, pixelX, pixelY, MarkerRadius, PlayerColor);
            overlayTexture.Apply();
        }

        private void WorldToPixel(Vector3 worldPos, out int x, out int y)
        {
            // Get position relative to map origin
            float relativeX = worldPos.x - worldOrigin.x;
            float relativeZ = worldPos.z - worldOrigin.z;

            // Convert to pixel coordinates using scale factors
            x = Mathf.RoundToInt(relativeX * pixelsPerWorldUnitX);
            y = Mathf.RoundToInt(relativeZ * pixelsPerWorldUnitZ);

            // Clamp to texture bounds
            x = Mathf.Clamp(x, 0, textureWidth - 1);
            y = Mathf.Clamp(y, 0, textureHeight - 1);
        }

        public void Clear()
        {
            if (overlayTexture == null) return;
            TextureGraphics2D.ClearTexture(overlayTexture);
            overlayTexture.Apply();
        }
    }
}