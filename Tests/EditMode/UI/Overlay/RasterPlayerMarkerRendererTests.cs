using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.UI;
using UnityEngine.TestTools;
using UnityEngine;
using UnityEngine.UI;

namespace Tsvrc.Tests.EditMode
{
    // RasterPlayerMarkerRenderer is the optional, reusable "paint into a texture" style renderer:
    // it owns the world-to-pixel projection, the Texture2D/RawImage/pixel-buffer plumbing, and
    // the flush-only-when-dirty optimization.
    public class RasterPlayerMarkerRendererTests : ProcessTestBase
    {
        private RecordingRasterPlayerMarkerRenderer CreateRenderer()
        {
            var renderer = CreateComponent<RecordingRasterPlayerMarkerRenderer>();
            renderer.OverlayImage = renderer.gameObject.AddComponent<RawImage>();
            return renderer;
        }

        private static Texture2D GetTexture(RasterPlayerMarkerRenderer renderer) =>
            PrivateFieldAccess.GetField<Texture2D>(renderer, "_overlayTexture");

        private static Color32[] GetPixelBuffer(RasterPlayerMarkerRenderer renderer) =>
            PrivateFieldAccess.GetField<Color32[]>(renderer, "_pixelBuffer");

        private static bool AllTransparent(Color32[] pixels)
        {
            foreach (Color32 p in pixels)
                if (p.a != 0) return false;
            return true;
        }

        [Test]
        public void Setup_NullOverlayImage_LogsErrorAndDoesNotConfigure()
        {
            var renderer = CreateComponent<RecordingRasterPlayerMarkerRenderer>();

            LogAssert.Expect(LogType.Error, "[TsVRC] [RecordingRasterPlayerMarkerRenderer] OverlayImage is not assigned.");
            renderer.Setup(10, 10, Vector3.zero, 1f, 1f);

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(renderer, "_isConfigured"));
        }

        [Test]
        public void Setup_NonPositiveWidth_LogsErrorAndDoesNotConfigure()
        {
            var renderer = CreateRenderer();

            LogAssert.Expect(LogType.Error, "[TsVRC] [RecordingRasterPlayerMarkerRenderer] Texture dimensions must be positive.");
            renderer.Setup(0, 10, Vector3.zero, 1f, 1f);

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(renderer, "_isConfigured"));
        }

        [Test]
        public void Setup_NonPositiveUnitsPerGridX_LogsErrorAndDoesNotConfigure()
        {
            var renderer = CreateRenderer();

            LogAssert.Expect(LogType.Error, "[TsVRC] [RecordingRasterPlayerMarkerRenderer] UnitsPerGrid values must be positive.");
            renderer.Setup(10, 10, Vector3.zero, 0f, 1f);

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(renderer, "_isConfigured"));
        }

        [Test]
        public void Setup_Valid_AssignsTextureAndWhiteColorToOverlayImage()
        {
            var renderer = CreateRenderer();

            renderer.Setup(10, 10, Vector3.zero, 1f, 1f);

            Assert.AreSame(GetTexture(renderer), renderer.OverlayImage.texture);
            Assert.AreEqual(Color.white, renderer.OverlayImage.color);
            Assert.IsTrue(AllTransparent(GetTexture(renderer).GetPixels32()));
        }

        [Test]
        public void OnMarkerVisible_NotConfigured_IsNoOpWithoutThrowing()
        {
            var renderer = CreateRenderer();

            Assert.DoesNotThrow(() => renderer.OnMarkerVisible(Vector3.zero, false, 0f, "A#1"));

            Assert.AreEqual(0, renderer.DrawMarkerCount);
        }

        [Test]
        public void OnMarkerVisible_ProjectsWorldPositionToPixelAndCallsDrawMarker()
        {
            var renderer = CreateRenderer();
            renderer.Setup(10, 10, Vector3.zero, 1f, 1f);

            renderer.OnMarkerVisible(new Vector3(5f, 0f, 5f), true, 45f, "Local#1");

            Assert.AreEqual(1, renderer.DrawMarkerCount);
            Assert.AreEqual(5, renderer.PixelXArgs[0]);
            Assert.AreEqual(5, renderer.PixelYArgs[0]);
            Assert.IsTrue(renderer.IsLocalPlayerArgs[0]);
            Assert.AreEqual(45f, renderer.HeadingArgs[0]);
            Assert.AreEqual("Local#1", renderer.PlayerIdArgs[0]);
        }

        [Test]
        public void OnMarkerVisible_PositionOutsideGrid_ClampsToBounds()
        {
            var renderer = CreateRenderer();
            renderer.Setup(10, 10, Vector3.zero, 1f, 1f);

            renderer.OnMarkerVisible(new Vector3(-5f, 0f, 500f), false, 0f, "A#1");

            Assert.AreEqual(0, renderer.PixelXArgs[0]);
            Assert.AreEqual(9, renderer.PixelYArgs[0]);
        }

        [Test]
        public void OnPresent_NothingDrawnSinceConfigure_SkipsFlush()
        {
            var renderer = CreateRenderer();
            renderer.Setup(10, 10, Vector3.zero, 1f, 1f);

            Assert.DoesNotThrow(() => renderer.OnPresent());

            Assert.IsTrue(AllTransparent(GetTexture(renderer).GetPixels32()));
        }

        [Test]
        public void OnPresent_AfterMarkerDrawn_FlushesDirtyBuffer()
        {
            var renderer = CreateRenderer();
            renderer.Setup(10, 10, Vector3.zero, 1f, 1f);
            Color32[] buffer = GetPixelBuffer(renderer);
            buffer[0] = Color.red; // simulate DrawMarker having painted something
            renderer.OnMarkerVisible(Vector3.zero, false, 0f, "A#1");

            renderer.OnPresent();

            Assert.AreEqual((Color32)Color.red, GetTexture(renderer).GetPixels32()[0]);
        }

        [Test]
        public void OnPresent_HideAfterDirtyShow_ClearsAndFlushes()
        {
            var renderer = CreateRenderer();
            renderer.Setup(10, 10, Vector3.zero, 1f, 1f);
            Color32[] buffer = GetPixelBuffer(renderer);
            buffer[0] = Color.red;
            renderer.OnMarkerVisible(Vector3.zero, false, 0f, "A#1");
            renderer.OnPresent(); // show: flush

            renderer.OnPresent(); // hide: nothing new drawn since last present

            Assert.IsTrue(AllTransparent(GetTexture(renderer).GetPixels32()));
        }
    }
}
