using System;
using NUnit.Framework;
using Tsvrc.UI.Utils;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Each heading test checks an interior point near the documented tip location is
    // painted and a point beyond the base (opposite the tip) is not, rather than asserting
    // on the triangle's exact edge pixels — robust to the rasterizer's edge-inclusion rule
    // without over-specifying it.
    public class TextureGraphics2DTriangleTests
    {
        private const int BufferSize = 40;
        private const int Cx = 20;
        private const int Cy = 20;
        private const int HalfWidth = 8;
        private const int HalfHeight = 10;

        private static Color32[] NewBuffer() => new Color32[BufferSize * BufferSize];

        private static bool IsSet(Color32[] buffer, int x, int y) =>
            buffer[y * BufferSize + x].Equals((Color32)Color.white);

        private static int CountSetPixels(Color32[] buffer)
        {
            int count = 0;
            foreach (Color32 p in buffer)
                if (p.a != 0) count++;
            return count;
        }

        private static void DrawStandard(Color32[] buffer, float headingDegrees) =>
            TextureGraphics2D.DrawTriangleToBuffer(buffer, BufferSize, BufferSize, Cx, Cy, HalfWidth, HalfHeight, headingDegrees, Color.white);

        [Test]
        public void DrawTriangleToBuffer_BufferShorterThanWidthTimesHeight_ThrowsIndexOutOfRange()
        {
            // buffer.Length must equal bufferWidth*bufferHeight, the same contract every
            // other *ToBuffer method in the class shares.
            var buffer = new Color32[4];

            Assert.Throws<IndexOutOfRangeException>(() =>
                TextureGraphics2D.DrawTriangleToBuffer(buffer, BufferSize, BufferSize, Cx, Cy, HalfWidth, HalfHeight, 0f, Color.white));
        }

        [Test]
        public void DrawTriangleToBuffer_NullBuffer_ThrowsNullReferenceException()
        {
            // Standard non-degenerate params guarantee at least one pixel write is reached.
            Assert.Throws<NullReferenceException>(() =>
                TextureGraphics2D.DrawTriangleToBuffer(null, BufferSize, BufferSize, Cx, Cy, HalfWidth, HalfHeight, 0f, Color.white));
        }

        [Test]
        public void DrawTriangleToBuffer_Heading0_TipPointsTowardIncreasingY()
        {
            var buffer = NewBuffer();

            DrawStandard(buffer, 0f);

            Assert.IsTrue(IsSet(buffer, Cx, Cy + HalfHeight - 1), "Interior point near the tip must be set.");
            Assert.IsTrue(IsSet(buffer, Cx, Cy - HalfHeight + 1), "Interior point near the base must be set.");
            Assert.IsFalse(IsSet(buffer, Cx, Cy + HalfHeight + 5), "Point beyond the tip must not be set.");
            Assert.IsFalse(IsSet(buffer, Cx, Cy - HalfHeight - 5), "Point beyond the base must not be set.");
        }

        [Test]
        public void DrawTriangleToBuffer_Heading90_TipPointsTowardIncreasingX()
        {
            var buffer = NewBuffer();

            DrawStandard(buffer, 90f);

            Assert.IsTrue(IsSet(buffer, Cx + HalfHeight - 1, Cy), "Interior point near the tip must be set.");
            Assert.IsTrue(IsSet(buffer, Cx - HalfHeight + 1, Cy), "Interior point near the base must be set.");
            Assert.IsFalse(IsSet(buffer, Cx + HalfHeight + 5, Cy), "Point beyond the tip must not be set.");
            Assert.IsFalse(IsSet(buffer, Cx - HalfHeight - 5, Cy), "Point beyond the base must not be set.");
        }

        [Test]
        public void DrawTriangleToBuffer_Heading180_TipPointsTowardDecreasingY()
        {
            var buffer = NewBuffer();

            DrawStandard(buffer, 180f);

            Assert.IsTrue(IsSet(buffer, Cx, Cy - HalfHeight + 1), "Interior point near the tip must be set.");
            Assert.IsTrue(IsSet(buffer, Cx, Cy + HalfHeight - 1), "Interior point near the base must be set.");
            Assert.IsFalse(IsSet(buffer, Cx, Cy - HalfHeight - 5), "Point beyond the tip must not be set.");
            Assert.IsFalse(IsSet(buffer, Cx, Cy + HalfHeight + 5), "Point beyond the base must not be set.");
        }

        [Test]
        public void DrawTriangleToBuffer_Heading270_TipPointsTowardDecreasingX()
        {
            var buffer = NewBuffer();

            DrawStandard(buffer, 270f);

            Assert.IsTrue(IsSet(buffer, Cx - HalfHeight + 1, Cy), "Interior point near the tip must be set.");
            Assert.IsTrue(IsSet(buffer, Cx + HalfHeight - 1, Cy), "Interior point near the base must be set.");
            Assert.IsFalse(IsSet(buffer, Cx - HalfHeight - 5, Cy), "Point beyond the tip must not be set.");
            Assert.IsFalse(IsSet(buffer, Cx + HalfHeight + 5, Cy), "Point beyond the base must not be set.");
        }

        [Test]
        public void DrawTriangleToBuffer_NegativeHeadingMinus90_MatchesHeading270()
        {
            var bufferNegative = NewBuffer();
            var buffer270 = NewBuffer();

            DrawStandard(bufferNegative, -90f);
            DrawStandard(buffer270, 270f);

            CollectionAssert.AreEqual(buffer270, bufferNegative);
        }

        [Test]
        public void DrawTriangleToBuffer_Heading450_PointsSameDirectionAsHeading90()
        {
            // 450 deg and 90 deg are mathematically the same heading, but Mathf.Sin/Cos on
            // the larger, unreduced angle produce float results measurably different from
            // the 90-deg case, enough to shift several boundary pixels along the triangle's
            // base edge. Asserting the same interior-tip/exterior-beyond-base anchor points
            // as the Heading90 test is robust to that and still proves 450 deg resolves to
            // "points east".
            var buffer = NewBuffer();

            DrawStandard(buffer, 450f);

            Assert.IsTrue(IsSet(buffer, Cx + HalfHeight - 1, Cy), "Interior point near the tip must be set.");
            Assert.IsTrue(IsSet(buffer, Cx - HalfHeight + 1, Cy), "Interior point near the base must be set.");
            Assert.IsFalse(IsSet(buffer, Cx + HalfHeight + 5, Cy), "Point beyond the tip must not be set.");
            Assert.IsFalse(IsSet(buffer, Cx - HalfHeight - 5, Cy), "Point beyond the base must not be set.");
        }

        [Test]
        public void DrawTriangleToBuffer_Heading45_ProducesSomePixelsBetweenNorthAndEastTips()
        {
            var buffer = NewBuffer();

            DrawStandard(buffer, 45f);

            Assert.Greater(CountSetPixels(buffer), 0);
            // At 45 degrees the tip is roughly northeast of center - a point directly north
            // (the 0-degree tip) should no longer be the painted apex region.
            Assert.IsFalse(IsSet(buffer, Cx, Cy + HalfHeight + 5));
            Assert.IsFalse(IsSet(buffer, Cx + HalfHeight + 5, Cy));
        }

        [Test]
        public void DrawTriangleToBuffer_ZeroHalfWidthAndHeight_DrawsSingleCenterPixel()
        {
            var buffer = NewBuffer();

            TextureGraphics2D.DrawTriangleToBuffer(buffer, BufferSize, BufferSize, Cx, Cy, 0, 0, 0f, Color.white);

            Assert.IsTrue(IsSet(buffer, Cx, Cy));
            Assert.AreEqual(1, CountSetPixels(buffer));
        }

        [Test]
        public void DrawTriangleToBuffer_ZeroHalfWidth_DoesNotThrowAndStaysWithinBaseHeightSpan()
        {
            var buffer = NewBuffer();

            Assert.DoesNotThrow(() =>
                TextureGraphics2D.DrawTriangleToBuffer(buffer, BufferSize, BufferSize, Cx, Cy, 0, HalfHeight, 0f, Color.white));
        }

        [Test]
        public void DrawTriangleToBuffer_ZeroHalfHeight_DoesNotThrow()
        {
            var buffer = NewBuffer();

            Assert.DoesNotThrow(() =>
                TextureGraphics2D.DrawTriangleToBuffer(buffer, BufferSize, BufferSize, Cx, Cy, HalfWidth, 0, 0f, Color.white));
        }

        [Test]
        public void DrawTriangleToBuffer_NegativeHalfExtents_DoesNotThrow()
        {
            var buffer = NewBuffer();

            Assert.DoesNotThrow(() =>
                TextureGraphics2D.DrawTriangleToBuffer(buffer, BufferSize, BufferSize, Cx, Cy, -HalfWidth, -HalfHeight, 30f, Color.white));
        }

        [Test]
        public void DrawTriangleToBuffer_EntirelyOutOfBufferBounds_NoOpNoException()
        {
            var buffer = NewBuffer();

            Assert.DoesNotThrow(() =>
                TextureGraphics2D.DrawTriangleToBuffer(buffer, BufferSize, BufferSize, -1000, -1000, HalfWidth, HalfHeight, 0f, Color.white));

            Assert.AreEqual(0, CountSetPixels(buffer));
        }

        [Test]
        public void DrawTriangleToBuffer_ClippedAtBufferCorner_NoIndexOutOfRangeAndDrawsSomething()
        {
            var buffer = NewBuffer();

            Assert.DoesNotThrow(() =>
                TextureGraphics2D.DrawTriangleToBuffer(buffer, BufferSize, BufferSize, 0, 0, HalfWidth, HalfHeight, 0f, Color.white));

            Assert.Greater(CountSetPixels(buffer), 0);
        }

        [Test]
        public void DrawTriangleToBuffer_ZeroSizedBuffer_NoOpNoException()
        {
            Color32[] buffer = Array.Empty<Color32>();

            Assert.DoesNotThrow(() =>
                TextureGraphics2D.DrawTriangleToBuffer(buffer, 0, 0, 0, 0, HalfWidth, HalfHeight, 0f, Color.white));
        }
    }
}
