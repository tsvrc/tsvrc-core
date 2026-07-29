using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.UI.TsPlayerPositionOverlay
{
    // _OnRemoteTick/_OnBlinkTick resolve a real VRCPlayerApi's world position (GetPosition/
    // GetRotation/isLocal) to a pixel - untestable in Edit Mode, where VRCPlayerApi does not
    // resolve at all. Both ticks are SendCustomEventDelayedSeconds-scheduled (empty method body
    // on the plain C# UdonSharpBehaviour base class in both modes), so they are invoked directly
    // below rather than waited on.
    public class TsPlayerPositionOverlayPlayModeTests : ProcessPlayModeTestBase
    {
        private GameObject _imageGameObject;

        [TearDown]
        public void TearDown()
        {
            if (_imageGameObject != null) Object.DestroyImmediate(_imageGameObject);
        }

        private Tsvrc.UI.TsPlayerPositionOverlay CreateOverlay()
        {
            var overlay = CreateProcess<Tsvrc.UI.TsPlayerPositionOverlay>();
            overlay.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            _imageGameObject = new GameObject("OverlayImage");
            overlay.OverlayImage = _imageGameObject.AddComponent<RawImage>();
            return overlay;
        }

        [UnityTest]
        public IEnumerator OnRemoteTick_RealTrackedPlayer_CachesActualWorldPositionAsPixel()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("Tracked");
            yield return null;
            yield return null;
            VRCPlayerApi remote = ClientSimPlayerEnvironment.FindPlayerByName("Tracked");
            Assert.IsNotNull(remote, "Setup sanity check: Tracked was not found.");
            // ClientSim remote players don't respond to TeleportTo - their position never
            // changes - so this asserts against their real, actual spawned position rather than
            // an assumed teleported one.
            Vector3 realPos = remote.GetPosition();
            string trackedId = remote.displayName + "#" + remote.playerId;

            var overlay = CreateOverlay();
            overlay.Setup(100, 100, Vector3.zero, 1f, 1f);
            overlay.StartOverlay(new[] { trackedId });

            overlay._OnRemoteTick();

            int cachedCount = PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount");
            int[] cachedPx = PrivateFieldAccess.GetField<int[]>(overlay, "_cachedPxArr");
            int[] cachedPy = PrivateFieldAccess.GetField<int[]>(overlay, "_cachedPyArr");
            int expectedPx = Mathf.Clamp(Mathf.RoundToInt(realPos.x), 0, 99);
            int expectedPy = Mathf.Clamp(Mathf.RoundToInt(realPos.z), 0, 99);
            Assert.AreEqual(1, cachedCount);
            Assert.AreEqual(expectedPx, cachedPx[0],
                "The cached pixel X must reflect the real player's actual current world position.");
            Assert.AreEqual(expectedPy, cachedPy[0],
                "The cached pixel Y must reflect the real player's actual current world position.");
        }

        [UnityTest]
        public IEnumerator OnBlinkTick_RealTrackedLocalPlayer_DrawsAtActualCurrentPosition()
        {
            yield return StartClientSim();
            Networking.LocalPlayer.TeleportTo(new Vector3(5f, 0f, 7f), Networking.LocalPlayer.GetRotation(),
                VRC_SceneDescriptor.SpawnOrientation.Default, false);
            string localId = Networking.LocalPlayer.displayName + "#" + Networking.LocalPlayer.playerId;

            var overlay = CreateOverlay();
            overlay.Setup(100, 100, Vector3.zero, 1f, 1f);
            overlay.StartOverlay(new[] { localId });

            overlay._OnRemoteTick();
            overlay._OnBlinkTick();

            Color32[] buffer = PrivateFieldAccess.GetField<Color32[]>(overlay, "_pixelBuffer");
            int width = PrivateFieldAccess.GetField<int>(overlay, "_textureWidth");
            int index = 7 * width + 5;
            Assert.AreNotEqual(0, buffer[index].a,
                "The local player marker must be drawn at the pixel matching their real, actual current world position.");
        }
    }
}
