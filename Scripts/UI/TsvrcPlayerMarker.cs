using Tsvrc.Core;
using Tsvrc.UI.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace Tsvrc.UI
{
    public class TsvrcPlayerMarker : TsvrcBehaviour
    {
        public RawImage PlayerDisplay;
        public Color PlayerColor = Color.yellow;
        public int MarkerRadius = 6;

        private Texture2D playerTexture;
        private int textureWidth;
        private int textureHeight;

        /// <summary>
        /// Initialize the player marker texture with given dimensions
        /// </summary>
        public void Initialize(int width, int height)
        {
            if (PlayerDisplay == null) return;

            textureWidth = width;
            textureHeight = height;

            playerTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);

            // Fill with transparent pixels
            Color[] pixels = new Color[textureWidth * textureHeight];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            playerTexture.SetPixels(pixels);
            playerTexture.Apply();

            PlayerDisplay.texture = playerTexture;
        }

        /// <summary>
        /// Update player marker position at given pixel coordinates
        /// </summary>
        public void UpdatePosition(int pixelX, int pixelY)
        {
            if (playerTexture == null || PlayerDisplay == null) return;

            // Clamp to texture bounds
            pixelX = Mathf.Clamp(pixelX, 0, textureWidth - 1);
            pixelY = Mathf.Clamp(pixelY, 0, textureHeight - 1);

            // Clear previous marker
            TextureGraphics2D.ClearTexture(playerTexture);

            // Draw new marker at position
            TextureGraphics2D.DrawCircle(playerTexture, pixelX, pixelY, MarkerRadius, PlayerColor);

            playerTexture.Apply();
        }

        /// <summary>
        /// Clear the player marker from display
        /// </summary>
        public void Clear()
        {
            if (playerTexture == null) return;

            TextureGraphics2D.ClearTexture(playerTexture);
            playerTexture.Apply();
        }
    }
}