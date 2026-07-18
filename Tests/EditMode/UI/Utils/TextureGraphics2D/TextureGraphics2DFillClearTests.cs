using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.UI.Utils;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    public class TextureGraphics2DFillClearTests
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
            _textures.Add(texture);
            return texture;
        }

        private static bool AllPixelsEqual(Color32[] pixels, Color32 expected)
        {
            foreach (Color32 p in pixels)
                if (!p.Equals(expected)) return false;
            return true;
        }

        [Test]
        public void FillTexture_NullTexture_ThrowsNullReferenceException()
        {
            // texture.width is accessed as the first statement (to size the temp pixel
            // array), so a null texture surfaces immediately as an NRE.
            Assert.Throws<NullReferenceException>(() => TextureGraphics2D.FillTexture(null, Color.red));
        }

        [Test]
        public void ClearTexture_NullTexture_ThrowsNullReferenceException()
        {
            Assert.Throws<NullReferenceException>(() => TextureGraphics2D.ClearTexture(null));
        }

        [Test]
        public void FillTexture_SolidColor_SetsAllPixelsToThatColor()
        {
            Texture2D texture = CreateTexture(4, 4);

            TextureGraphics2D.FillTexture(texture, Color.red);

            Assert.IsTrue(AllPixelsEqual(texture.GetPixels32(), (Color32)Color.red));
        }

        [Test]
        public void FillTexture_1x1_SetsSinglePixel()
        {
            Texture2D texture = CreateTexture(1, 1);

            TextureGraphics2D.FillTexture(texture, Color.blue);

            Assert.AreEqual((Color32)Color.blue, texture.GetPixels32()[0]);
        }

        [Test]
        public void FillTexture_TransparentColor_SetsAllPixelsTransparent()
        {
            Texture2D texture = CreateTexture(3, 3);
            TextureGraphics2D.FillTexture(texture, Color.white);

            TextureGraphics2D.FillTexture(texture, new Color(0f, 0f, 0f, 0f));

            Assert.IsTrue(AllPixelsEqual(texture.GetPixels32(), new Color32(0, 0, 0, 0)));
        }

        [Test]
        public void FillTexture_PartialAlphaColor_PreservesAlphaChannel()
        {
            Texture2D texture = CreateTexture(2, 2);
            var translucentGreen = new Color(0f, 1f, 0f, 0.5f);

            TextureGraphics2D.FillTexture(texture, translucentGreen);

            Assert.AreEqual((Color32)translucentGreen, texture.GetPixels32()[0]);
        }

        [Test]
        public void FillTexture_CalledTwice_SecondCallOverwritesFirst()
        {
            Texture2D texture = CreateTexture(2, 2);
            TextureGraphics2D.FillTexture(texture, Color.red);

            TextureGraphics2D.FillTexture(texture, Color.green);

            Assert.IsTrue(AllPixelsEqual(texture.GetPixels32(), (Color32)Color.green));
        }

        [Test]
        public void ClearTexture_AfterFill_AllPixelsBecomeFullyTransparent()
        {
            Texture2D texture = CreateTexture(4, 4);
            TextureGraphics2D.FillTexture(texture, Color.magenta);

            TextureGraphics2D.ClearTexture(texture);

            Assert.IsTrue(AllPixelsEqual(texture.GetPixels32(), new Color32(0, 0, 0, 0)));
        }

        [Test]
        public void ClearTexture_1x1_ClearsSinglePixel()
        {
            Texture2D texture = CreateTexture(1, 1);
            TextureGraphics2D.FillTexture(texture, Color.white);

            TextureGraphics2D.ClearTexture(texture);

            Assert.AreEqual(new Color32(0, 0, 0, 0), texture.GetPixels32()[0]);
        }

        [Test]
        public void ClearTexture_AlreadyClear_StaysTransparent()
        {
            Texture2D texture = CreateTexture(3, 3);

            TextureGraphics2D.ClearTexture(texture);

            Assert.IsTrue(AllPixelsEqual(texture.GetPixels32(), new Color32(0, 0, 0, 0)));
        }

        [Test]
        public void ClearBuffer_FilledBuffer_ZeroesAllElements()
        {
            var buffer = new Color32[16];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = Color.red;

            TextureGraphics2D.ClearBuffer(buffer);

            Assert.IsTrue(AllPixelsEqual(buffer, new Color32(0, 0, 0, 0)));
        }

        [Test]
        public void ClearBuffer_ZeroLengthBuffer_DoesNotThrow()
        {
            var buffer = Array.Empty<Color32>();

            Assert.DoesNotThrow(() => TextureGraphics2D.ClearBuffer(buffer));
        }

        [Test]
        public void ClearBuffer_NullBuffer_ThrowsNullReferenceException()
        {
            // buffer.Length is evaluated (to pass as Array.Clear's count argument) before
            // Array.Clear itself runs, so the null access surfaces as an NRE, not an ANE.
            Assert.Throws<NullReferenceException>(() => TextureGraphics2D.ClearBuffer(null));
        }

        [Test]
        public void FillBuffer_SetsAllElementsToGivenColor()
        {
            var buffer = new Color32[9];

            TextureGraphics2D.FillBuffer(buffer, Color.cyan);

            Assert.IsTrue(AllPixelsEqual(buffer, (Color32)Color.cyan));
        }

        [Test]
        public void FillBuffer_ZeroLengthBuffer_DoesNotThrow()
        {
            var buffer = Array.Empty<Color32>();

            Assert.DoesNotThrow(() => TextureGraphics2D.FillBuffer(buffer, Color.red));
        }

        [Test]
        public void FillBuffer_NullBuffer_ThrowsNullReferenceException()
        {
            Assert.Throws<NullReferenceException>(() => TextureGraphics2D.FillBuffer(null, Color.red));
        }

        [Test]
        public void FillBuffer_CalledTwiceWithDifferentColors_LastCallWins()
        {
            var buffer = new Color32[4];
            TextureGraphics2D.FillBuffer(buffer, Color.red);

            TextureGraphics2D.FillBuffer(buffer, Color.blue);

            Assert.IsTrue(AllPixelsEqual(buffer, (Color32)Color.blue));
        }

        [Test]
        public void FlushBuffer_UploadsBufferContentsToTexture()
        {
            Texture2D texture = CreateTexture(2, 2);
            var buffer = new Color32[4];
            TextureGraphics2D.FillBuffer(buffer, Color.yellow);

            TextureGraphics2D.FlushBuffer(texture, buffer);

            Assert.IsTrue(AllPixelsEqual(texture.GetPixels32(), (Color32)Color.yellow));
        }

        [Test]
        public void FlushBuffer_PreservesPerPixelVariationNotJustSolidFill()
        {
            Texture2D texture = CreateTexture(2, 1);
            var buffer = new[] { (Color32)Color.red, (Color32)Color.blue };

            TextureGraphics2D.FlushBuffer(texture, buffer);

            Color32[] result = texture.GetPixels32();
            Assert.AreEqual((Color32)Color.red, result[0]);
            Assert.AreEqual((Color32)Color.blue, result[1]);
        }

        [Test]
        public void FlushBuffer_NullTexture_ThrowsNullReferenceException()
        {
            var buffer = new Color32[4];

            Assert.Throws<NullReferenceException>(() => TextureGraphics2D.FlushBuffer(null, buffer));
        }

        [Test]
        public void FlushBuffer_TextureWithoutMipChain_DoesNotThrowWhenSkippingMipRecalculation()
        {
            // FlushBuffer calls Apply(false) — the "skip mip recalculation" contract documented
            // on the method. A texture created with mipChain:false must not error/throw here.
            Texture2D texture = CreateTexture(4, 4);
            var buffer = new Color32[16];
            TextureGraphics2D.FillBuffer(buffer, Color.white);

            Assert.DoesNotThrow(() => TextureGraphics2D.FlushBuffer(texture, buffer));
        }
    }
}
