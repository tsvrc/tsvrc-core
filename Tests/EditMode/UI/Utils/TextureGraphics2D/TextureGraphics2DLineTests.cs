using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.UI.Utils;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Unity's Test Framework fails a test if it logs an unexpected error, so the
    // *_ClampsWithoutLoggingError tests below need no explicit log assertion: an
    // out-of-bounds Texture2D.SetPixel call would fail the test on its own.
    public class TextureGraphics2DLineTests
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

        private static bool IsSet(Texture2D texture, int x, int y) =>
            texture.GetPixel(x, y).Equals(Color.white);

        private static int CountSetPixels(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            int count = 0;
            foreach (Color32 p in pixels)
                if (p.a != 0) count++;
            return count;
        }

        [Test]
        public void DrawLine_NullTexture_ThrowsNullReferenceException()
        {
            // texture.width is read inside the per-point bounds check, reached even for a
            // single-point (same start/end), thickness-1 line.
            Assert.Throws<System.NullReferenceException>(() => TextureGraphics2D.DrawLine(null, 0, 0, 0, 0, 1, Color.white));
        }

        [Test]
        public void DrawLine_Horizontal_DrawsExactSpan()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawLine(texture, 1, 2, 5, 2, 1, Color.white);

            for (int x = 1; x <= 5; x++)
                Assert.IsTrue(IsSet(texture, x, 2), $"Expected pixel ({x},2) to be set.");
            Assert.AreEqual(5, CountSetPixels(texture));
        }

        [Test]
        public void DrawLine_Vertical_DrawsExactSpan()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawLine(texture, 3, 1, 3, 4, 1, Color.white);

            for (int y = 1; y <= 4; y++)
                Assert.IsTrue(IsSet(texture, 3, y));
            Assert.AreEqual(4, CountSetPixels(texture));
        }

        [Test]
        public void DrawLine_DiagonalPositiveSlope_FollowsBresenhamPath()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawLine(texture, 0, 0, 3, 3, 1, Color.white);

            Assert.IsTrue(IsSet(texture, 0, 0));
            Assert.IsTrue(IsSet(texture, 1, 1));
            Assert.IsTrue(IsSet(texture, 2, 2));
            Assert.IsTrue(IsSet(texture, 3, 3));
            Assert.AreEqual(4, CountSetPixels(texture));
        }

        [TestCase(5, 5, 2, 2)]  // negative sx, negative sy
        [TestCase(2, 5, 5, 2)]  // positive sx, negative sy
        [TestCase(5, 2, 2, 5)]  // negative sx, positive sy
        [TestCase(2, 2, 5, 5)]  // positive sx, positive sy
        public void DrawLine_AllFourDiagonalQuadrants_IncludesBothEndpoints(int x0, int y0, int x1, int y1)
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawLine(texture, x0, y0, x1, y1, 1, Color.white);

            Assert.IsTrue(IsSet(texture, x0, y0), "Start point must be drawn.");
            Assert.IsTrue(IsSet(texture, x1, y1), "End point must be drawn.");
        }

        [Test]
        public void DrawLine_SameStartAndEndPoint_DrawsSinglePixel()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawLine(texture, 4, 4, 4, 4, 1, Color.white);

            Assert.IsTrue(IsSet(texture, 4, 4));
            Assert.AreEqual(1, CountSetPixels(texture));
        }

        [Test]
        public void DrawLine_ZeroThickness_StillDrawsSinglePixelWidthLine()
        {
            // thickness/2 == 0 for thickness 0, so the inner brush loop still runs exactly
            // once (tx=0,ty=0) — same as thickness=1.
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawLine(texture, 1, 1, 4, 1, 0, Color.white);

            for (int x = 1; x <= 4; x++)
                Assert.IsTrue(IsSet(texture, x, 1));
            Assert.AreEqual(4, CountSetPixels(texture));
        }

        [Test]
        public void DrawLine_ThicknessTwo_DrawsThreePixelWideBrush()
        {
            // -thickness/2..thickness/2 for thickness=2 is -1..1 inclusive: 3 pixels, not 2.
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawLine(texture, 5, 5, 5, 5, 2, Color.white);

            for (int y = 4; y <= 6; y++)
                for (int x = 4; x <= 6; x++)
                    Assert.IsTrue(IsSet(texture, x, y), $"Expected ({x},{y}) set for thickness-2 brush.");
            Assert.AreEqual(9, CountSetPixels(texture));
        }

        [Test]
        public void DrawLine_EntirelyOutOfBounds_DrawsNothingWithoutThrowing()
        {
            Texture2D texture = CreateTexture(5, 5);

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawLine(texture, -50, -50, -40, -40, 1, Color.white));

            Assert.AreEqual(0, CountSetPixels(texture));
        }

        [Test]
        public void DrawLine_PartiallyOutOfBounds_ClipsSilentlyAtTextureEdge()
        {
            Texture2D texture = CreateTexture(5, 5);

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawLine(texture, -3, 2, 3, 2, 1, Color.white));

            for (int x = 0; x <= 3; x++)
                Assert.IsTrue(IsSet(texture, x, 2));
            Assert.AreEqual(4, CountSetPixels(texture));
        }

        [Test]
        public void DrawHorizontalLine_NullTexture_ThrowsNullReferenceException()
        {
            // texture.width/height are only read once length>0 and thickness>0 let the
            // inner loop body run - use non-zero values so the null access is reached.
            Assert.Throws<System.NullReferenceException>(() => TextureGraphics2D.DrawHorizontalLine(null, 0, 0, 1, 1, Color.white));
        }

        [Test]
        public void DrawHorizontalLine_Basic_DrawsExactSpan()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawHorizontalLine(texture, 2, 3, 4, 1, Color.white);

            for (int x = 2; x < 6; x++)
                Assert.IsTrue(IsSet(texture, x, 3));
            Assert.AreEqual(4, CountSetPixels(texture));
        }

        [Test]
        public void DrawHorizontalLine_ThicknessGreaterThanOne_DrawsMultipleRows()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawHorizontalLine(texture, 1, 1, 3, 2, Color.white);

            for (int y = 1; y <= 2; y++)
                for (int x = 1; x <= 3; x++)
                    Assert.IsTrue(IsSet(texture, x, y));
            Assert.AreEqual(6, CountSetPixels(texture));
        }

        [Test]
        public void DrawHorizontalLine_ZeroThickness_DrawsNothing()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawHorizontalLine(texture, 1, 1, 5, 0, Color.white);

            Assert.AreEqual(0, CountSetPixels(texture));
        }

        [Test]
        public void DrawHorizontalLine_ZeroLength_DrawsNothing()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawHorizontalLine(texture, 1, 1, 0, 3, Color.white);

            Assert.AreEqual(0, CountSetPixels(texture));
        }

        [Test]
        public void DrawHorizontalLine_NegativeX_ClampsAtLeftEdgeWithoutLoggingError()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawHorizontalLine(texture, -3, 1, 5, 1, Color.white);

            Assert.IsTrue(IsSet(texture, 0, 1));
            Assert.IsTrue(IsSet(texture, 1, 1));
            Assert.AreEqual(2, CountSetPixels(texture));
        }

        [Test]
        public void DrawHorizontalLine_NegativeY_ClampsAtTopEdgeWithoutLoggingError()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawHorizontalLine(texture, 1, -2, 2, 5, Color.white);

            for (int y = 0; y <= 2; y++)
                for (int x = 1; x <= 2; x++)
                    Assert.IsTrue(IsSet(texture, x, y));
            Assert.AreEqual(6, CountSetPixels(texture));
        }

        [Test]
        public void DrawHorizontalLine_PartiallyOutOfBoundsRight_ClipsAtTextureWidth()
        {
            Texture2D texture = CreateTexture(5, 5);

            TextureGraphics2D.DrawHorizontalLine(texture, 3, 1, 5, 1, Color.white);

            Assert.IsTrue(IsSet(texture, 3, 1));
            Assert.IsTrue(IsSet(texture, 4, 1));
            Assert.AreEqual(2, CountSetPixels(texture));
        }

        [Test]
        public void DrawHorizontalLine_EntireLineNegative_DrawsNothing()
        {
            Texture2D texture = CreateTexture(5, 5);

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawHorizontalLine(texture, -10, -10, 3, 1, Color.white));

            Assert.AreEqual(0, CountSetPixels(texture));
        }

        [Test]
        public void DrawVerticalLine_NullTexture_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TextureGraphics2D.DrawVerticalLine(null, 0, 0, 1, 1, Color.white));
        }

        [Test]
        public void DrawVerticalLine_Basic_DrawsExactSpan()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawVerticalLine(texture, 2, 3, 4, 1, Color.white);

            for (int y = 3; y < 7; y++)
                Assert.IsTrue(IsSet(texture, 2, y));
            Assert.AreEqual(4, CountSetPixels(texture));
        }

        [Test]
        public void DrawVerticalLine_ThicknessGreaterThanOne_DrawsMultipleColumns()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawVerticalLine(texture, 1, 1, 3, 2, Color.white);

            for (int y = 1; y <= 3; y++)
                for (int x = 1; x <= 2; x++)
                    Assert.IsTrue(IsSet(texture, x, y));
            Assert.AreEqual(6, CountSetPixels(texture));
        }

        [Test]
        public void DrawVerticalLine_ZeroThickness_DrawsNothing()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawVerticalLine(texture, 1, 1, 5, 0, Color.white);

            Assert.AreEqual(0, CountSetPixels(texture));
        }

        [Test]
        public void DrawVerticalLine_ZeroLength_DrawsNothing()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawVerticalLine(texture, 1, 1, 0, 3, Color.white);

            Assert.AreEqual(0, CountSetPixels(texture));
        }

        [Test]
        public void DrawVerticalLine_NegativeY_ClampsAtTopEdgeWithoutLoggingError()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawVerticalLine(texture, 1, -3, 5, 1, Color.white);

            Assert.IsTrue(IsSet(texture, 1, 0));
            Assert.IsTrue(IsSet(texture, 1, 1));
            Assert.AreEqual(2, CountSetPixels(texture));
        }

        [Test]
        public void DrawVerticalLine_NegativeX_ClampsAtLeftEdgeWithoutLoggingError()
        {
            Texture2D texture = CreateTexture(10, 10);

            TextureGraphics2D.DrawVerticalLine(texture, -2, 1, 2, 5, Color.white);

            for (int x = 0; x <= 2; x++)
                for (int y = 1; y <= 2; y++)
                    Assert.IsTrue(IsSet(texture, x, y));
            Assert.AreEqual(6, CountSetPixels(texture));
        }

        [Test]
        public void DrawVerticalLine_PartiallyOutOfBoundsBottom_ClipsAtTextureHeight()
        {
            Texture2D texture = CreateTexture(5, 5);

            TextureGraphics2D.DrawVerticalLine(texture, 1, 3, 5, 1, Color.white);

            Assert.IsTrue(IsSet(texture, 1, 3));
            Assert.IsTrue(IsSet(texture, 1, 4));
            Assert.AreEqual(2, CountSetPixels(texture));
        }

        [Test]
        public void DrawVerticalLine_EntireLineNegative_DrawsNothing()
        {
            Texture2D texture = CreateTexture(5, 5);

            Assert.DoesNotThrow(() => TextureGraphics2D.DrawVerticalLine(texture, -10, -10, 3, 1, Color.white));

            Assert.AreEqual(0, CountSetPixels(texture));
        }
    }
}
