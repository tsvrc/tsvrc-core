using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.UI.Utils;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // The buffer and texture overloads of each line-drawing method clamp bounds
    // symmetrically on both edges, so several tests below assert they produce
    // pixel-for-pixel identical output for the same input, including negative-origin cases.
    public class TextureGraphics2DBufferLineTests
    {
        private readonly List<Texture2D> _textures = new List<Texture2D>();

        [TearDown]
        public void TearDown()
        {
            foreach (Texture2D texture in _textures)
                if (texture != null)
                    Object.DestroyImmediate(texture);
            _textures.Clear();
        }

        private Texture2D CreateTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            TextureGraphics2D.ClearTexture(texture);
            _textures.Add(texture);
            return texture;
        }

        private static bool IsSet(Color32[] buffer, int width, int x, int y) =>
            buffer[y * width + x].Equals((Color32)Color.white);

        private static int CountSetPixels(Color32[] buffer)
        {
            int count = 0;
            foreach (Color32 p in buffer)
                if (p.a != 0) count++;
            return count;
        }

        [Test]
        public void DrawLineToBuffer_NullBuffer_ThrowsNullReferenceException()
        {
            // Single point, thickness 1 -> half=0 -> the ty/tx loops still run once and hit
            // the buffer write.
            Assert.Throws<System.NullReferenceException>(
                () => TextureGraphics2D.DrawLineToBuffer(null, 10, 10, 0, 0, 0, 0, 1, Color.white));
        }

        [Test]
        public void DrawLineToBuffer_BufferShorterThanWidthTimesHeight_ThrowsIndexOutOfRange()
        {
            // buffer.Length must equal bufferWidth*bufferHeight, the same contract every
            // other *ToBuffer method in the class shares.
            var buffer = new Color32[4];

            Assert.Throws<System.IndexOutOfRangeException>(
                () => TextureGraphics2D.DrawLineToBuffer(buffer, 10, 10, 5, 5, 5, 5, 1, Color.white));
        }

        [Test]
        public void DrawLineToBuffer_ZeroSizedBuffer_NoOpNoException()
        {
            var buffer = System.Array.Empty<Color32>();

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawLineToBuffer(buffer, 0, 0, 0, 0, 3, 3, 1, Color.white));
        }

        [Test]
        public void DrawLineToBuffer_Horizontal_DrawsExactSpan()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawLineToBuffer(buffer, 10, 10, 1, 2, 5, 2, 1, Color.white);

            for (int x = 1; x <= 5; x++)
                Assert.IsTrue(IsSet(buffer, 10, x, 2));
            Assert.AreEqual(5, CountSetPixels(buffer));
        }

        [Test]
        public void DrawLineToBuffer_DiagonalPositiveSlope_FollowsBresenhamPath()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawLineToBuffer(buffer, 10, 10, 0, 0, 3, 3, 1, Color.white);

            Assert.IsTrue(IsSet(buffer, 10, 0, 0));
            Assert.IsTrue(IsSet(buffer, 10, 1, 1));
            Assert.IsTrue(IsSet(buffer, 10, 2, 2));
            Assert.IsTrue(IsSet(buffer, 10, 3, 3));
            Assert.AreEqual(4, CountSetPixels(buffer));
        }

        [Test]
        public void DrawLineToBuffer_SameStartAndEndPoint_DrawsSinglePixel()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawLineToBuffer(buffer, 10, 10, 4, 4, 4, 4, 1, Color.white);

            Assert.IsTrue(IsSet(buffer, 10, 4, 4));
            Assert.AreEqual(1, CountSetPixels(buffer));
        }

        [Test]
        public void DrawLineToBuffer_ZeroThickness_StillDrawsSinglePixelWidthLine()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawLineToBuffer(buffer, 10, 10, 1, 1, 4, 1, 0, Color.white);

            Assert.AreEqual(4, CountSetPixels(buffer));
        }

        [Test]
        public void DrawLineToBuffer_ThicknessTwo_DrawsThreePixelWideBrush()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawLineToBuffer(buffer, 10, 10, 5, 5, 5, 5, 2, Color.white);

            Assert.AreEqual(9, CountSetPixels(buffer));
        }

        [Test]
        public void DrawLineToBuffer_EntirelyOutOfBounds_NoOpNoException()
        {
            var buffer = new Color32[5 * 5];

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawLineToBuffer(buffer, 5, 5, -50, -50, -40, -40, 1, Color.white));

            Assert.AreEqual(0, CountSetPixels(buffer));
        }

        [Test]
        public void DrawLineToBuffer_MatchesDrawLine_PixelForPixel_PositiveCoordinates()
        {
            const int width = 15;
            const int height = 15;
            Texture2D texture = CreateTexture(width, height);
            var buffer = new Color32[width * height];

            TextureGraphics2D.DrawLine(texture, 2, 3, 12, 9, 2, Color.white);
            TextureGraphics2D.DrawLineToBuffer(buffer, width, height, 2, 3, 12, 9, 2, Color.white);

            CollectionAssert.AreEqual(texture.GetPixels32(), buffer);
        }

        [Test]
        public void DrawLineToBuffer_MatchesDrawLine_PixelForPixel_NegativeOriginClipped()
        {
            const int width = 10;
            const int height = 10;
            Texture2D texture = CreateTexture(width, height);
            var buffer = new Color32[width * height];

            TextureGraphics2D.DrawLine(texture, -3, -3, 6, 6, 3, Color.white);
            TextureGraphics2D.DrawLineToBuffer(buffer, width, height, -3, -3, 6, 6, 3, Color.white);

            CollectionAssert.AreEqual(texture.GetPixels32(), buffer);
        }

        [Test]
        public void DrawHorizontalLineToBuffer_NullBuffer_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(
                () => TextureGraphics2D.DrawHorizontalLineToBuffer(null, 10, 10, 0, 0, 1, 1, Color.white));
        }

        [Test]
        public void DrawHorizontalLineToBuffer_BufferShorterThanWidthTimesHeight_ThrowsIndexOutOfRange()
        {
            var buffer = new Color32[4];

            Assert.Throws<System.IndexOutOfRangeException>(
                () => TextureGraphics2D.DrawHorizontalLineToBuffer(buffer, 10, 10, 5, 5, 2, 1, Color.white));
        }

        [Test]
        public void DrawHorizontalLineToBuffer_ZeroSizedBuffer_NoOpNoException()
        {
            var buffer = System.Array.Empty<Color32>();

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawHorizontalLineToBuffer(buffer, 0, 0, 0, 0, 3, 1, Color.white));
        }

        [Test]
        public void DrawHorizontalLineToBuffer_Basic_DrawsExactSpan()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawHorizontalLineToBuffer(buffer, 10, 10, 2, 3, 4, 1, Color.white);

            for (int x = 2; x < 6; x++)
                Assert.IsTrue(IsSet(buffer, 10, x, 3));
            Assert.AreEqual(4, CountSetPixels(buffer));
        }

        [Test]
        public void DrawHorizontalLineToBuffer_ZeroThickness_DrawsNothing()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawHorizontalLineToBuffer(buffer, 10, 10, 1, 1, 5, 0, Color.white);

            Assert.AreEqual(0, CountSetPixels(buffer));
        }

        [Test]
        public void DrawHorizontalLineToBuffer_ZeroLength_DrawsNothing()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawHorizontalLineToBuffer(buffer, 10, 10, 1, 1, 0, 3, Color.white);

            Assert.AreEqual(0, CountSetPixels(buffer));
        }

        [Test]
        public void DrawHorizontalLineToBuffer_NegativeX_ClampsAtLeftEdge()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawHorizontalLineToBuffer(buffer, 10, 10, -3, 1, 5, 1, Color.white);

            Assert.IsTrue(IsSet(buffer, 10, 0, 1));
            Assert.IsTrue(IsSet(buffer, 10, 1, 1));
            Assert.AreEqual(2, CountSetPixels(buffer));
        }

        [Test]
        public void DrawHorizontalLineToBuffer_MatchesDrawHorizontalLine_PixelForPixel_IncludingNegativeOrigin()
        {
            // The texture overload and the buffer overload must produce identical pixel
            // sets even when x/y are negative, not just when fully in-bounds.
            const int width = 10;
            const int height = 10;
            Texture2D texture = CreateTexture(width, height);
            var buffer = new Color32[width * height];

            TextureGraphics2D.DrawHorizontalLine(texture, -4, -2, 8, 5, Color.white);
            TextureGraphics2D.DrawHorizontalLineToBuffer(buffer, width, height, -4, -2, 8, 5, Color.white);

            CollectionAssert.AreEqual(texture.GetPixels32(), buffer);
        }

        [Test]
        public void DrawHorizontalLineToBuffer_MatchesDrawHorizontalLine_PixelForPixel_PositiveCoordinates()
        {
            const int width = 12;
            const int height = 12;
            Texture2D texture = CreateTexture(width, height);
            var buffer = new Color32[width * height];

            TextureGraphics2D.DrawHorizontalLine(texture, 2, 3, 6, 2, Color.white);
            TextureGraphics2D.DrawHorizontalLineToBuffer(buffer, width, height, 2, 3, 6, 2, Color.white);

            CollectionAssert.AreEqual(texture.GetPixels32(), buffer);
        }

        [Test]
        public void DrawVerticalLineToBuffer_NullBuffer_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(
                () => TextureGraphics2D.DrawVerticalLineToBuffer(null, 10, 10, 0, 0, 1, 1, Color.white));
        }

        [Test]
        public void DrawVerticalLineToBuffer_BufferShorterThanWidthTimesHeight_ThrowsIndexOutOfRange()
        {
            var buffer = new Color32[4];

            Assert.Throws<System.IndexOutOfRangeException>(
                () => TextureGraphics2D.DrawVerticalLineToBuffer(buffer, 10, 10, 5, 5, 2, 1, Color.white));
        }

        [Test]
        public void DrawVerticalLineToBuffer_ZeroSizedBuffer_NoOpNoException()
        {
            var buffer = System.Array.Empty<Color32>();

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawVerticalLineToBuffer(buffer, 0, 0, 0, 0, 3, 1, Color.white));
        }

        [Test]
        public void DrawVerticalLineToBuffer_Basic_DrawsExactSpan()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawVerticalLineToBuffer(buffer, 10, 10, 2, 3, 4, 1, Color.white);

            for (int y = 3; y < 7; y++)
                Assert.IsTrue(IsSet(buffer, 10, 2, y));
            Assert.AreEqual(4, CountSetPixels(buffer));
        }

        [Test]
        public void DrawVerticalLineToBuffer_ZeroThickness_DrawsNothing()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawVerticalLineToBuffer(buffer, 10, 10, 1, 1, 5, 0, Color.white);

            Assert.AreEqual(0, CountSetPixels(buffer));
        }

        [Test]
        public void DrawVerticalLineToBuffer_ZeroLength_DrawsNothing()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawVerticalLineToBuffer(buffer, 10, 10, 1, 1, 0, 3, Color.white);

            Assert.AreEqual(0, CountSetPixels(buffer));
        }

        [Test]
        public void DrawVerticalLineToBuffer_NegativeY_ClampsAtTopEdge()
        {
            var buffer = new Color32[10 * 10];

            TextureGraphics2D.DrawVerticalLineToBuffer(buffer, 10, 10, 1, -3, 5, 1, Color.white);

            Assert.IsTrue(IsSet(buffer, 10, 1, 0));
            Assert.IsTrue(IsSet(buffer, 10, 1, 1));
            Assert.AreEqual(2, CountSetPixels(buffer));
        }

        [Test]
        public void DrawVerticalLineToBuffer_MatchesDrawVerticalLine_PixelForPixel_IncludingNegativeOrigin()
        {
            const int width = 10;
            const int height = 10;
            Texture2D texture = CreateTexture(width, height);
            var buffer = new Color32[width * height];

            TextureGraphics2D.DrawVerticalLine(texture, -2, -4, 8, 5, Color.white);
            TextureGraphics2D.DrawVerticalLineToBuffer(buffer, width, height, -2, -4, 8, 5, Color.white);

            CollectionAssert.AreEqual(texture.GetPixels32(), buffer);
        }

        [Test]
        public void DrawVerticalLineToBuffer_MatchesDrawVerticalLine_PixelForPixel_PositiveCoordinates()
        {
            const int width = 12;
            const int height = 12;
            Texture2D texture = CreateTexture(width, height);
            var buffer = new Color32[width * height];

            TextureGraphics2D.DrawVerticalLine(texture, 3, 2, 6, 2, Color.white);
            TextureGraphics2D.DrawVerticalLineToBuffer(buffer, width, height, 3, 2, 6, 2, Color.white);

            CollectionAssert.AreEqual(texture.GetPixels32(), buffer);
        }

        [Test]
        public void DrawVerticalLineToBuffer_EntirelyOutOfBounds_NoOpNoException()
        {
            var buffer = new Color32[5 * 5];

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawVerticalLineToBuffer(buffer, 5, 5, -50, -50, 3, 1, Color.white));

            Assert.AreEqual(0, CountSetPixels(buffer));
        }
    }
}
