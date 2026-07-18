using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.UI.Utils;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // DrawCircle and DrawCircleToBuffer implement the same circle equation through two
    // different bounds strategies (per-pixel check vs. pre-clamped loop bounds), so several
    // tests below assert they produce pixel-for-pixel identical output for the same input.
    public class TextureGraphics2DCircleTests
    {
        private readonly List<Texture2D> _textures = new List<Texture2D>();

        [TearDown]
        public void TearDown()
        {
            foreach (Texture2D texture in _textures)
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            _textures.Clear();
        }

        private Texture2D CreateTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            TextureGraphics2D.ClearTexture(texture);
            _textures.Add(texture);
            return texture;
        }

        private static bool IsSet(Texture2D texture, int x, int y) =>
            texture.GetPixel(x, y).Equals(Color.white);

        private static int CountSetPixels(Color32[] pixels)
        {
            int count = 0;
            foreach (Color32 p in pixels)
                if (p.a != 0) count++;
            return count;
        }

        [Test]
        public void DrawCircle_NullTexture_ThrowsNullReferenceException()
        {
            // radius=0 still enters the body (0<=0) and reads texture.width in the bounds check.
            Assert.Throws<NullReferenceException>(() => TextureGraphics2D.DrawCircle(null, 5, 5, 0, Color.white));
        }

        [Test]
        public void DrawCircle_RadiusZero_DrawsSingleCenterPixel()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawCircle(texture, 5, 5, 0, Color.white);

            Assert.IsTrue(IsSet(texture, 5, 5));
            Assert.AreEqual(1, CountSetPixels(texture.GetPixels32()));
        }

        [Test]
        public void DrawCircle_RadiusOne_DrawsPlusShapeExcludingDiagonals()
        {
            // i*i+j*j<=1 excludes the four diagonal corners (1+1=2>1).
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawCircle(texture, 5, 5, 1, Color.white);

            Assert.IsTrue(IsSet(texture, 5, 5));
            Assert.IsTrue(IsSet(texture, 4, 5));
            Assert.IsTrue(IsSet(texture, 6, 5));
            Assert.IsTrue(IsSet(texture, 5, 4));
            Assert.IsTrue(IsSet(texture, 5, 6));
            Assert.IsFalse(IsSet(texture, 4, 4), "Diagonal corner must be excluded at radius 1.");
            Assert.AreEqual(5, CountSetPixels(texture.GetPixels32()));
        }

        [Test]
        public void DrawCircle_NegativeRadius_DrawsNothing()
        {
            Texture2D texture = CreateTexture(10, 10);

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawCircle(texture, 5, 5, -3, Color.white));

            Assert.AreEqual(0, CountSetPixels(texture.GetPixels32()));
        }

        [Test]
        public void DrawCircle_CenterNearCorner_ClipsToVisibleQuadrant()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawCircle(texture, 0, 0, 2, Color.white);

            Assert.IsTrue(IsSet(texture, 0, 0));
            Assert.IsTrue(IsSet(texture, 1, 0));
            Assert.IsTrue(IsSet(texture, 0, 1));
            Assert.Greater(CountSetPixels(texture.GetPixels32()), 0);
        }

        [Test]
        public void DrawCircle_EntirelyOutOfBounds_DrawsNothingWithoutThrowing()
        {
            Texture2D texture = CreateTexture(5, 5);

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawCircle(texture, -50, -50, 3, Color.white));

            Assert.AreEqual(0, CountSetPixels(texture.GetPixels32()));
        }

        [Test]
        public void DrawCircle_EdgePixelExactlyOnBoundary_IsIncluded()
        {
            // 3-4-5 right triangle: (3,4) is exactly at distance 5 from center for radius 5.
            Texture2D texture = CreateTexture(20, 20);

            TextureGraphics2D.DrawCircle(texture, 10, 10, 5, Color.white);

            Assert.IsTrue(IsSet(texture, 13, 14), "Point exactly at radius (3-4-5 triangle) must be included (<=, not <).");
        }

        [Test]
        public void DrawCircle_JustOutsideBoundary_IsExcluded()
        {
            Texture2D texture = CreateTexture(20, 20);

            TextureGraphics2D.DrawCircle(texture, 10, 10, 5, Color.white);

            // Distance from (10,10) to (16,10) is 6 > 5.
            Assert.IsFalse(IsSet(texture, 16, 10));
        }

        [Test]
        public void DrawCircle_LargeRadius_FillsWholeSmallTexture()
        {
            Texture2D texture = CreateTexture(4, 4);

            TextureGraphics2D.DrawCircle(texture, 2, 2, 100, Color.white);

            Assert.AreEqual(16, CountSetPixels(texture.GetPixels32()));
        }

        [Test]
        public void DrawCircleToBuffer_NullBuffer_ThrowsNullReferenceException()
        {
            Assert.Throws<NullReferenceException>(
                () => TextureGraphics2D.DrawCircleToBuffer(null, 10, 10, 5, 5, 0, Color.white));
        }

        [Test]
        public void DrawCircleToBuffer_RadiusZero_SetsSingleCenterPixel()
        {
            var buffer = new Color32[100];

            TextureGraphics2D.DrawCircleToBuffer(buffer, 10, 10, 5, 5, 0, Color.white);

            Assert.AreEqual((Color32)Color.white, buffer[5 * 10 + 5]);
            Assert.AreEqual(1, CountSetPixels(buffer));
        }

        [Test]
        public void DrawCircleToBuffer_NegativeRadius_NoOp()
        {
            var buffer = new Color32[100];

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawCircleToBuffer(buffer, 10, 10, 5, 5, -4, Color.white));

            Assert.AreEqual(0, CountSetPixels(buffer));
        }

        [Test]
        public void DrawCircleToBuffer_EntirelyOffBuffer_NoOpNoException()
        {
            var buffer = new Color32[100];

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawCircleToBuffer(buffer, 10, 10, -100, -100, 3, Color.white));

            Assert.AreEqual(0, CountSetPixels(buffer));
        }

        [Test]
        public void DrawCircleToBuffer_ClipsAtBufferEdge_NoIndexOutOfRange()
        {
            var buffer = new Color32[100];

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawCircleToBuffer(buffer, 10, 10, 0, 0, 5, Color.white));

            Assert.Greater(CountSetPixels(buffer), 0);
        }

        [Test]
        public void DrawCircleToBuffer_ZeroSizedBuffer_NoOpNoException()
        {
            Color32[] buffer = Array.Empty<Color32>();

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawCircleToBuffer(buffer, 0, 0, 0, 0, 3, Color.white));
        }

        [Test]
        public void DrawCircleToBuffer_BufferShorterThanWidthTimesHeight_ThrowsIndexOutOfRange()
        {
            // buffer.Length must equal bufferWidth*bufferHeight; the method does not defend
            // against a mismatch.
            var buffer = new Color32[4];

            Assert.Throws<System.IndexOutOfRangeException>(
                () => TextureGraphics2D.DrawCircleToBuffer(buffer, 10, 10, 5, 5, 3, Color.white));
        }

        [Test]
        public void DrawCircleToBuffer_MatchesDrawCircle_PixelForPixel()
        {
            const int width = 20;
            const int height = 20;
            Texture2D texture = CreateTexture(width, height);
            var buffer = new Color32[width * height];

            TextureGraphics2D.DrawCircle(texture, 10, 8, 6, Color.white);
            TextureGraphics2D.DrawCircleToBuffer(buffer, width, height, 10, 8, 6, Color.white);

            Color32[] textureResult = texture.GetPixels32();
            CollectionAssert.AreEqual(textureResult, buffer);
        }

        [Test]
        public void DrawCircleToBuffer_ClippedNearCorner_MatchesDrawCirclePixelForPixel()
        {
            const int width = 10;
            const int height = 10;
            Texture2D texture = CreateTexture(width, height);
            var buffer = new Color32[width * height];

            TextureGraphics2D.DrawCircle(texture, 0, 0, 4, Color.white);
            TextureGraphics2D.DrawCircleToBuffer(buffer, width, height, 0, 0, 4, Color.white);

            CollectionAssert.AreEqual(texture.GetPixels32(), buffer);
        }
    }
}
