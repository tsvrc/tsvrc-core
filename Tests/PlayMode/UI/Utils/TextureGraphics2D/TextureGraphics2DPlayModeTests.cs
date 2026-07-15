using System.Collections;
using NUnit.Framework;
using Tsvrc.UI.Utils;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.PlayMode
{
    // This suite is a supplementary smoke check that the same draw -> flush round trip
    // behaves identically while a real Play Mode session is running (Texture2D
    // creation/GC can differ subtly across the Edit/Play boundary). Unity's Test Runner
    // does not reliably report Play Mode pass/fail in this project's batch-mode setup, so
    // each test computes its own bool and logs exactly one PLAYMODE_TEST_RESULT marker
    // instead of relying on the Test Runner's green/red indicator — verify via Console log
    // output, not the runner UI.
    public class TextureGraphics2DPlayModeTests
    {
        private Texture2D _texture;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_texture != null)
                Object.DestroyImmediate(_texture);
            _texture = null;
            yield return null;
        }

        private Texture2D CreateTexture(int width, int height)
        {
            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            return _texture;
        }

        private static void LogResult(string testName, bool passed, string details) =>
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);

        private static bool AllPixelsEqual(Color32[] pixels, Color32 expected)
        {
            foreach (Color32 p in pixels)
                if (!p.Equals(expected)) return false;
            return true;
        }

        [UnityTest]
        public IEnumerator FillTexture_InPlayMode_SetsAllPixelsToColor()
        {
            const string testName = nameof(FillTexture_InPlayMode_SetsAllPixelsToColor);
            Texture2D texture = CreateTexture(8, 8);

            TextureGraphics2D.FillTexture(texture, Color.red);
            yield return null;

            bool passed = AllPixelsEqual(texture.GetPixels32(), (Color32)Color.red);
            LogResult(testName, passed, "pixel0=" + texture.GetPixels32()[0]);
        }

        [UnityTest]
        public IEnumerator ClearTexture_InPlayMode_ClearsToFullyTransparent()
        {
            const string testName = nameof(ClearTexture_InPlayMode_ClearsToFullyTransparent);
            Texture2D texture = CreateTexture(8, 8);
            TextureGraphics2D.FillTexture(texture, Color.blue);
            yield return null;

            TextureGraphics2D.ClearTexture(texture);
            yield return null;

            bool passed = AllPixelsEqual(texture.GetPixels32(), new Color32(0, 0, 0, 0));
            LogResult(testName, passed, "pixel0=" + texture.GetPixels32()[0]);
        }

        [UnityTest]
        public IEnumerator DrawLine_InPlayMode_DrawsExpectedDiagonal()
        {
            const string testName = nameof(DrawLine_InPlayMode_DrawsExpectedDiagonal);
            Texture2D texture = CreateTexture(10, 10);
            TextureGraphics2D.ClearTexture(texture);
            yield return null;

            TextureGraphics2D.DrawLine(texture, 0, 0, 4, 4, 1, Color.white);
            yield return null;

            Color32[] pixels = texture.GetPixels32();
            bool passed = pixels[0 * 10 + 0].Equals((Color32)Color.white) &&
                pixels[4 * 10 + 4].Equals((Color32)Color.white) &&
                pixels[2 * 10 + 2].Equals((Color32)Color.white);
            LogResult(testName, passed, "diagCorners=" + pixels[0] + "/" + pixels[44]);
        }

        [UnityTest]
        public IEnumerator DrawHorizontalLine_InPlayMode_NegativeXClampsWithoutError()
        {
            const string testName = nameof(DrawHorizontalLine_InPlayMode_NegativeXClampsWithoutError);
            Texture2D texture = CreateTexture(10, 10);
            TextureGraphics2D.ClearTexture(texture);
            yield return null;

            bool threw = false;
            try
            {
                TextureGraphics2D.DrawHorizontalLine(texture, -3, 1, 5, 1, Color.white);
            }
            catch
            {
                threw = true;
            }
            yield return null;

            Color32[] pixels = texture.GetPixels32();
            bool passed = !threw && pixels[1 * 10 + 0].Equals((Color32)Color.white) &&
                pixels[1 * 10 + 1].Equals((Color32)Color.white);
            LogResult(testName, passed, "threw=" + threw);
        }

        [UnityTest]
        public IEnumerator DrawCircleToBuffer_InPlayMode_FlushesExpectedPixelsToTexture()
        {
            const string testName = nameof(DrawCircleToBuffer_InPlayMode_FlushesExpectedPixelsToTexture);
            const int size = 10;
            Texture2D texture = CreateTexture(size, size);
            var buffer = new Color32[size * size];
            yield return null;

            TextureGraphics2D.DrawCircleToBuffer(buffer, size, size, 5, 5, 2, Color.white);
            TextureGraphics2D.FlushBuffer(texture, buffer);
            yield return null;

            Color32[] pixels = texture.GetPixels32();
            bool passed = pixels[5 * size + 5].Equals((Color32)Color.white);
            LogResult(testName, passed, "center=" + pixels[5 * size + 5]);
        }

        [UnityTest]
        public IEnumerator DrawTriangleToBuffer_InPlayMode_PaintsTipRegionForZeroHeading()
        {
            const string testName = nameof(DrawTriangleToBuffer_InPlayMode_PaintsTipRegionForZeroHeading);
            const int size = 40;
            Texture2D texture = CreateTexture(size, size);
            var buffer = new Color32[size * size];
            yield return null;

            TextureGraphics2D.DrawTriangleToBuffer(buffer, size, size, 20, 20, 8, 10, 0f, Color.white);
            TextureGraphics2D.FlushBuffer(texture, buffer);
            yield return null;

            Color32[] pixels = texture.GetPixels32();
            bool passed = pixels[29 * size + 20].Equals((Color32)Color.white);
            LogResult(testName, passed, "tipPixel=" + pixels[29 * size + 20]);
        }

        [UnityTest]
        public IEnumerator ClearBufferThenFillBuffer_InPlayMode_RoundTripsCorrectly()
        {
            const string testName = nameof(ClearBufferThenFillBuffer_InPlayMode_RoundTripsCorrectly);
            var buffer = new Color32[16];
            TextureGraphics2D.FillBuffer(buffer, Color.green);
            yield return null;

            TextureGraphics2D.ClearBuffer(buffer);
            yield return null;

            bool passed = AllPixelsEqual(buffer, new Color32(0, 0, 0, 0));
            LogResult(testName, passed, "buffer0=" + buffer[0]);
        }
    }
}
