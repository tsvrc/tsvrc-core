using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.UI.PlayerPositionOverlay
{
    // _OnRemoteTick/_OnBlinkTick resolve a real VRCPlayerApi, untestable in Edit Mode. Both ticks
    // are invoked directly below rather than waited on. Every assertion is against a
    // RecordingPlayerMarkerRenderer test double.
    public class TsPlayerPositionOverlayPlayModeTests : ProcessPlayModeTestBase
    {
        private Tsvrc.UI.PlayerPositionOverlay CreateOverlay()
        {
            var overlay = CreateProcess<Tsvrc.UI.PlayerPositionOverlay>();
            overlay.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            overlay.Renderer = overlay.gameObject.AddComponent<RecordingPlayerMarkerRenderer>();
            return overlay;
        }

        private static RecordingPlayerMarkerRenderer GetRenderer(Tsvrc.UI.PlayerPositionOverlay overlay) =>
            (RecordingPlayerMarkerRenderer)overlay.Renderer;

        [UnityTest]
        public IEnumerator OnRemoteTick_RealTrackedPlayer_CachesActualWorldPosition()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Tracked");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("Tracked");
            Assert.IsNotNull(remote, "Setup sanity check: Tracked was not found.");
            Vector3 realPos = remote.GetPosition();
            string trackedId = remote.displayName + "#" + remote.playerId;

            var overlay = CreateOverlay();
            overlay.StartOverlay(new[] { trackedId });

            overlay._OnRemoteTick();

            int cachedCount = PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount");
            Vector3[] cachedPositions = PrivateFieldAccess.GetField<Vector3[]>(overlay, "_cachedWorldPositions");
            Assert.AreEqual(1, cachedCount);
            Assert.AreEqual(realPos, cachedPositions[0],
                "The cached position must reflect the real player's actual current world position.");
        }

        [UnityTest]
        public IEnumerator OnBlinkTick_RealTrackedLocalPlayer_CallsOnMarkerVisibleWithActualCurrentPosition()
        {
            yield return StartClientSim();
            Networking.LocalPlayer.TeleportTo(new Vector3(5f, 0f, 7f), Networking.LocalPlayer.GetRotation(),
                VRC_SceneDescriptor.SpawnOrientation.Default, false);
            string localId = Networking.LocalPlayer.displayName + "#" + Networking.LocalPlayer.playerId;

            var overlay = CreateOverlay();
            overlay.StartOverlay(new[] { localId });

            overlay._OnRemoteTick();
            overlay._OnBlinkTick();

            RecordingPlayerMarkerRenderer renderer = GetRenderer(overlay);
            Assert.AreEqual(1, renderer.OnMarkerVisibleCount);
            Assert.AreEqual(new Vector3(5f, 0f, 7f), renderer.WorldPositions[0],
                "The local player marker must be presented at their real, actual current world position.");
            Assert.IsTrue(renderer.IsLocalPlayerArgs[0]);
        }

        [UnityTest]
        public IEnumerator OnRemoteTick_RendererUsesHeadingTrue_CachesRealRotation()
        {
            yield return StartClientSim();
            Networking.LocalPlayer.TeleportTo(Networking.LocalPlayer.GetPosition(),
                Quaternion.Euler(0f, 90f, 0f), VRC_SceneDescriptor.SpawnOrientation.Default, false);
            string localId = Networking.LocalPlayer.displayName + "#" + Networking.LocalPlayer.playerId;

            var overlay = CreateOverlay();
            GetRenderer(overlay).UsesHeadingValue = true;
            overlay.StartOverlay(new[] { localId });

            overlay._OnRemoteTick();

            float[] cachedHeadings = PrivateFieldAccess.GetField<float[]>(overlay, "_cachedHeadings");
            Assert.AreEqual(90f, cachedHeadings[0], 0.01f,
                "When the renderer declares UsesHeading, the real GetRotation() value must be cached.");
        }

        [UnityTest]
        public IEnumerator OnRemoteTick_RendererUsesHeadingFalse_CachesZeroRegardlessOfRealRotation()
        {
            yield return StartClientSim();
            Networking.LocalPlayer.TeleportTo(Networking.LocalPlayer.GetPosition(),
                Quaternion.Euler(0f, 90f, 0f), VRC_SceneDescriptor.SpawnOrientation.Default, false);
            string localId = Networking.LocalPlayer.displayName + "#" + Networking.LocalPlayer.playerId;

            var overlay = CreateOverlay();
            GetRenderer(overlay).UsesHeadingValue = false;
            overlay.StartOverlay(new[] { localId });

            overlay._OnRemoteTick();

            float[] cachedHeadings = PrivateFieldAccess.GetField<float[]>(overlay, "_cachedHeadings");
            Assert.AreEqual(0f, cachedHeadings[0],
                "When the renderer declares it doesn't use heading, GetRotation() must be skipped entirely.");
        }
    }
}
