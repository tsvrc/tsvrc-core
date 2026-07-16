using System.Collections;
using NUnit.Framework;
using Tsvrc.Core.Generated;
using Tsvrc.Player;
using Tsvrc.Tests.Editor;
using Tsvrc.UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // Real ClientSim Play Mode tests for the one thing TsPlayerPositionOverlay can't
    // prove without a real player: that a real VRCPlayerApi.GetPosition() actually maps
    // to the correct pixel through the class's own world-to-pixel formula, and that a
    // real local vs. remote player is distinguished correctly. Edit Mode (Tests/Editor/UI/
    // TsPlayerPositionOverlay/) covers everything else - VRCPlayerApi.GetPlayerCount()/
    // Networking.LocalPlayer are safe (non-throwing) outside Play Mode but return an empty/
    // null result there, so exact pixel resolution needs a real running instance. Follows
    // the exact harness/methodology established by PlayerTrackerAbandonmentTests.cs: NUnit
    // Assert failures are silent in this project's Play Mode environment, so every test
    // computes its own bool and logs exactly one PLAYMODE_TEST_RESULT marker.
    //
    // Rather than assuming where ClientSim spawns a player, every test reads the player's
    // real GetPosition() and computes the expected pixel with the exact same formula the
    // class uses, then compares - robust regardless of the actual spawn point.
    public class TsPlayerPositionOverlayPlayModeTests
    {
        private const float PixelsPerUnit = 1f;
        private static readonly Vector3 WorldOrigin = new Vector3(-50f, 0f, -50f);
        private const int TextureSize = 100;

        private GameObject _descriptorObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (ClientSimMain.HasInstance())
                ClientSimMain.RemoveInstance();

            ClientSimRuntimeLoader.EndUnityTesting();

            if (_descriptorObject != null)
                Object.DestroyImmediate(_descriptorObject);

            yield return null;
        }

        private void CreateMinimalSceneDescriptor()
        {
            _descriptorObject = new GameObject("__TestSceneDescriptor");
            VRCSceneDescriptor descriptor = _descriptorObject.AddComponent<VRCSceneDescriptor>();

            GameObject spawnObject = new GameObject("__TestSpawn");
            spawnObject.transform.SetParent(_descriptorObject.transform);

            descriptor.spawns = new[] { spawnObject.transform };
        }

        private IEnumerator StartClientSim()
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool("com.vrchat.clientsim.session.accepted_warning", true);
#endif
            CreateMinimalSceneDescriptor();

            ClientSimSettings settings = new ClientSimSettings
            {
                enableClientSim = true,
                spawnPlayer = true,
                deleteEditorOnly = false,
                localPlayerIsMaster = true,
                initializationDelay = 0f,
            };

            ClientSimRuntimeLoader.BeginUnityTesting(settings);
            ClientSimRuntimeLoader.StartClientSim(settings);

            yield return null;
            yield return null;
        }

        private static VRCPlayerApi FindPlayerByName(string name)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            foreach (VRCPlayerApi p in players)
                if (p != null && p.displayName == name) return p;
            return null;
        }

        private static void LogResult(string testName, bool passed, string details)
        {
            Debug.Log("PLAYMODE_TEST_RESULT: " + testName + (passed ? " PASS " : " FAIL ") + details);
        }

        private static TsPlayerPositionOverlay CreateOverlay(string name)
        {
            var go = new GameObject(name);
            var overlay = go.AddComponent<TsPlayerPositionOverlay>();
            overlay.OverlayImage = go.AddComponent<RawImage>();
            overlay.TsConstruct((TsRoot)null);
            overlay.Setup(TextureSize, TextureSize, WorldOrigin, PixelsPerUnit, PixelsPerUnit);
            return overlay;
        }

        private static int ExpectedPx(Vector3 pos) =>
            Mathf.Clamp(Mathf.RoundToInt((pos.x - WorldOrigin.x) * PixelsPerUnit), 0, TextureSize - 1);

        private static int ExpectedPy(Vector3 pos) =>
            Mathf.Clamp(Mathf.RoundToInt((pos.z - WorldOrigin.z) * PixelsPerUnit), 0, TextureSize - 1);

        [UnityTest]
        public IEnumerator OnRemoteTick_RealLocalPlayer_ResolvesToPixelPositionMatchingItsActualWorldPosition()
        {
            const string testName = "OnRemoteTick_RealLocalPlayer_ResolvesToPixelPositionMatchingItsActualWorldPosition";

            yield return StartClientSim();

            var overlay = CreateOverlay("Overlay_LocalResolve");
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            overlay.StartOverlay(new[] { localId });

            overlay._OnRemoteTick();

            Vector3 realPos = Networking.LocalPlayer.GetPosition();
            int cachedCount = PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount");
            int[] cachedPx = PrivateFieldAccess.GetField<int[]>(overlay, "_cachedPxArr");
            int[] cachedPy = PrivateFieldAccess.GetField<int[]>(overlay, "_cachedPyArr");
            bool[] cachedIsLocal = PrivateFieldAccess.GetField<bool[]>(overlay, "_cachedIsLocalArr");

            Object.DestroyImmediate(overlay.gameObject);

            bool passed = cachedCount == 1 && cachedIsLocal[0] &&
                cachedPx[0] == ExpectedPx(realPos) && cachedPy[0] == ExpectedPy(realPos);
            LogResult(testName, passed, "cachedCount=" + cachedCount +
                " cachedPx=" + (cachedCount > 0 ? cachedPx[0].ToString() : "n/a") +
                " expectedPx=" + ExpectedPx(realPos));
        }

        [UnityTest]
        public IEnumerator OnRemoteTick_RealRemotePlayer_ResolvesAsNonLocalAtItsActualWorldPosition()
        {
            const string testName = "OnRemoteTick_RealRemotePlayer_ResolvesAsNonLocalAtItsActualWorldPosition";

            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RemoteResolve");
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName("RemoteResolve");
            Assert.IsNotNull(remote, "RemoteResolve was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);

            var overlay = CreateOverlay("Overlay_RemoteResolve");
            overlay.StartOverlay(new[] { remoteId });

            overlay._OnRemoteTick();

            Vector3 realPos = remote.GetPosition();
            int cachedCount = PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount");
            int[] cachedPx = PrivateFieldAccess.GetField<int[]>(overlay, "_cachedPxArr");
            int[] cachedPy = PrivateFieldAccess.GetField<int[]>(overlay, "_cachedPyArr");
            bool[] cachedIsLocal = PrivateFieldAccess.GetField<bool[]>(overlay, "_cachedIsLocalArr");

            Object.DestroyImmediate(overlay.gameObject);

            bool passed = cachedCount == 1 && !cachedIsLocal[0] &&
                cachedPx[0] == ExpectedPx(realPos) && cachedPy[0] == ExpectedPy(realPos);
            LogResult(testName, passed, "cachedCount=" + cachedCount +
                " cachedPx=" + (cachedCount > 0 ? cachedPx[0].ToString() : "n/a") +
                " expectedPx=" + ExpectedPx(realPos));
        }

        [UnityTest]
        public IEnumerator OnBlinkTick_RealLocalPlayerShow_PaintsLocalColorAtExpectedPixel()
        {
            const string testName = "OnBlinkTick_RealLocalPlayerShow_PaintsLocalColorAtExpectedPixel";

            yield return StartClientSim();

            var overlay = CreateOverlay("Overlay_LocalPaint");
            overlay.LocalPlayerShape = MarkerShape.Circle;
            overlay.LocalPlayerColor = Color.yellow;
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            overlay.StartOverlay(new[] { localId });
            overlay._OnRemoteTick();
            PrivateFieldAccess.SetField(overlay, "_scheduledBlinkTickCount", 1);

            overlay._OnBlinkTick();

            Vector3 realPos = Networking.LocalPlayer.GetPosition();
            int px = ExpectedPx(realPos);
            int py = ExpectedPy(realPos);
            Texture2D texture = PrivateFieldAccess.GetField<Texture2D>(overlay, "_overlayTexture");
            Color32 pixelAtPlayer = texture.GetPixels32()[py * TextureSize + px];

            Object.DestroyImmediate(overlay.gameObject);

            bool passed = pixelAtPlayer.r > 200 && pixelAtPlayer.g > 200 && pixelAtPlayer.a > 0;
            LogResult(testName, passed, "px=" + px + " py=" + py + " pixel=" + pixelAtPlayer);
        }

        [UnityTest]
        public IEnumerator PixelClamping_LocalPlayerTeleportedFarOutsideMappedArea_ClampsToTextureEdge()
        {
            const string testName = "PixelClamping_LocalPlayerTeleportedFarOutsideMappedArea_ClampsToTextureEdge";

            yield return StartClientSim();

            var overlay = CreateOverlay("Overlay_Clamping");
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            overlay.StartOverlay(new[] { localId });

            Networking.LocalPlayer.TeleportTo(new Vector3(100000f, 0f, 100000f), Quaternion.identity);
            yield return null;

            bool threw = false;
            try
            {
                overlay._OnRemoteTick();
            }
            catch
            {
                threw = true;
            }

            int cachedCount = PrivateFieldAccess.GetField<int>(overlay, "_cachedPlayerCount");
            int[] cachedPx = PrivateFieldAccess.GetField<int[]>(overlay, "_cachedPxArr");
            int[] cachedPy = PrivateFieldAccess.GetField<int[]>(overlay, "_cachedPyArr");

            Object.DestroyImmediate(overlay.gameObject);

            bool passed = !threw && cachedCount == 1 &&
                cachedPx[0] == TextureSize - 1 && cachedPy[0] == TextureSize - 1;
            LogResult(testName, passed, "threw=" + threw + " cachedCount=" + cachedCount +
                " px=" + (cachedCount > 0 ? cachedPx[0].ToString() : "n/a"));
        }

        [UnityTest]
        public IEnumerator StartOverlay_RealLocalAndRemotePlayers_TracksBothRealIds()
        {
            const string testName = "StartOverlay_RealLocalAndRemotePlayers_TracksBothRealIds";

            yield return StartClientSim();

            ClientSimMain.SpawnRemotePlayer("RemoteInStart");
            yield return null;
            yield return null;
            VRCPlayerApi remote = FindPlayerByName("RemoteInStart");
            Assert.IsNotNull(remote, "RemoteInStart was not spawned.");
            string remoteId = TsPlayer.GetPlayerID(remote);
            string localId = TsPlayer.GetPlayerID(Networking.LocalPlayer);

            var overlay = CreateOverlay("Overlay_StartBoth");
            overlay.StartOverlay(new[] { localId, remoteId });

            int lastPlayerIdsLength = overlay.LastPlayerIds.Length;
            bool passed = overlay.IsProcessRunning() &&
                System.Array.IndexOf(overlay.LastPlayerIds, localId) >= 0 &&
                System.Array.IndexOf(overlay.LastPlayerIds, remoteId) >= 0;

            Object.DestroyImmediate(overlay.gameObject);

            LogResult(testName, passed, "lastPlayerIdsLength=" + lastPlayerIdsLength);
        }
    }
}
