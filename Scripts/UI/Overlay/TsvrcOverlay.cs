using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.UI.Overlay
{
    /// <summary>
    /// Positions the overlay container to follow the local player's head in both VR and desktop.
    /// Scales with avatar eye height and applies VR-specific display offsets.
    /// This class handles only physical placement. View management lives in <see cref="TsvrcOverlayNavigator"/>.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TsvrcOverlay : TsvrcBehaviour
    {
        [Header("References")]
        [Tooltip("A transform placed at the notional head position inside the Container hierarchy. " +
                 "The container is repositioned so this child aligns with the real head.")]
        public Transform PlayerHeadPosition;
        [Tooltip("Root transform of the overlay UI hierarchy. Moved and scaled every frame.")]
        public Transform Container;
        [Tooltip("The display canvas transform. Receives VR-specific local position/rotation/scale on Start.")]
        public Transform Display;

        [Header("VR Display Placement")]
        public Vector3 VrPosition = new Vector3(0f, -0.015f, -0.255f);
        public Vector3 VrRotation = new Vector3(5f, 0f, 0f);
        public Vector3 VrScale = new Vector3(0.00004f, 0.00004f, 0.01f);

        private Vector3 _initialContainerScale;
        // Guards OnAvatarEyeHeightChanged against VRChat firing the join event before Start runs.
        private bool _initialized;
        // Cached in Start. Networking.LocalPlayer never changes during a session.
        private VRCPlayerApi _localPlayer;

        private void Start()
        {
            if (Container == null) return;

            _initialContainerScale = Container.localScale;
            _initialized = true; // must be set before OnAvatarEyeHeightChanged can fire

            // Disable colliders so the overlay does not physically interact with the world.
            foreach (var col in Container.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            _localPlayer = Networking.LocalPlayer;
            // Utilities.IsValid also catches VRCPlayerApi references that are non-null but invalid.
            if (!Utilities.IsValid(_localPlayer)) return;

            // Apply the initial scale. OnAvatarEyeHeightChanged keeps it in sync after this,
            // firing at join and on every avatar change. Clamped to 0.1 m (the SDK minimum).
            float eyeHeight = Mathf.Max(_localPlayer.GetAvatarEyeHeightAsMeters(), 0.1f);
            Container.localScale = _initialContainerScale * eyeHeight;

            if (_localPlayer.IsUserInVR() && Display != null)
            {
                Display.localPosition = VrPosition;
                Display.localEulerAngles = VrRotation;
                Display.localScale = VrScale;
            }
        }

        // VRChat fires this for every player when their avatar height changes, including at world join.
        // Only the local player's height affects the overlay scale.
        public override void OnAvatarEyeHeightChanged(VRCPlayerApi player, float prevEyeHeightAsMeters)
        {
            // _initialContainerScale is captured in Start, so ignore events that arrive before that.
            if (!_initialized || Container == null) return;
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            float eyeHeight = Mathf.Max(player.GetAvatarEyeHeightAsMeters(), 0.1f);
            Container.localScale = _initialContainerScale * eyeHeight;
        }

        // Using LateUpdate ensures we read the final head position after all other Update calls have settled.
        private void LateUpdate()
        {
            if (Container == null || !Container.gameObject.activeSelf) return;
            if (PlayerHeadPosition == null || !Utilities.IsValid(_localPlayer)) return;

            var head = _localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

            // Rotate first. Unity recalculates child world positions immediately,
            // so PlayerHeadPosition.position below already reflects the new rotation.
            Container.rotation = head.rotation;
            // Shift the Container so PlayerHeadPosition lands exactly at head.position.
            //   new position = head.position - (PlayerHeadPosition.position - Container.position)
            Container.position = head.position - (PlayerHeadPosition.position - Container.position);
            // Scale is managed by OnAvatarEyeHeightChanged, not updated per frame here.
        }

        public void OpenOverlay()
        {
            if (Container != null) Container.gameObject.SetActive(true);
        }

        public void CloseOverlay()
        {
            if (Container != null) Container.gameObject.SetActive(false);
        }

        public bool IsOpen => Container != null && Container.gameObject.activeSelf;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (Container != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(Container.position, Container.lossyScale);
            }
            if (PlayerHeadPosition != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(PlayerHeadPosition.position, 0.05f);
            }
            if (Display != null)
            {
                Gizmos.color = Color.cyan;
                DrawPanelGizmo(Display);
            }
        }

        private static void DrawPanelGizmo(Transform panel)
        {
            var rect = panel.GetComponent<RectTransform>();
            if (rect == null) return;
            var size = new Vector3(
                rect.rect.width * panel.lossyScale.x,
                rect.rect.height * panel.lossyScale.y,
                0.01f);
            Gizmos.matrix = Matrix4x4.TRS(panel.position, panel.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
