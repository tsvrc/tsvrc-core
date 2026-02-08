using Tsvrc.Core;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.UI.Overlay
{
    public class TsvrcOverlay : TsvrcBehaviour
    {
        [Header("References")]
        public Transform PlayerHeadPosition;
        public Transform Container;
        public Transform LeftPanel;
        public Transform RightPanel;
        public Transform MainPanel;

        private Vector3 _initialContainerScale;

        private void Start()
        {
            // Store the initial container scale set in the editor
            if (Container != null)
            {
                _initialContainerScale = Container.localScale;
            }

            // Disable all colliders to prevent physical collision
            if (Container != null)
            {
                Collider[] colliders = Container.GetComponentsInChildren<Collider>(true);
                foreach (Collider col in colliders)
                {
                    col.enabled = false;
                }
            }
        }

        private void Update()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            // Follow player head for both VR and desktop
            Vector3 headPosition = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Quaternion headRotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;

            // Position Container so that PlayerHeadPosition aligns with the real head
            Container.rotation = headRotation;
            Vector3 offset = PlayerHeadPosition.position - Container.position;
            Container.position = headPosition - offset;

            // Scale overlay based on player avatar eye height
            float eyeHeight = localPlayer.GetAvatarEyeHeightAsMeters();
            Container.localScale = _initialContainerScale * eyeHeight;
        }

        private void OnDrawGizmos()
        {
            // Draw container
            if (Container != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(Container.position, Container.lossyScale);
            }

            // Draw player head position
            if (PlayerHeadPosition != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(PlayerHeadPosition.position, 0.05f);
            }

            // Draw main panel
            if (MainPanel != null)
            {
                Gizmos.color = Color.cyan;
                DrawPanelGizmo(MainPanel);
            }

            // Draw left panel
            if (LeftPanel != null)
            {
                Gizmos.color = Color.magenta;
                DrawPanelGizmo(LeftPanel);
            }

            // Draw right panel
            if (RightPanel != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f); // Orange
                DrawPanelGizmo(RightPanel);
            }
        }

        private void DrawPanelGizmo(Transform panel)
        {
            RectTransform rect = panel.GetComponent<RectTransform>();
            if (rect != null)
            {
                Vector3 size = new Vector3(
                    rect.rect.width * panel.lossyScale.x,
                    rect.rect.height * panel.lossyScale.y,
                    0.01f
                );

                Gizmos.matrix = Matrix4x4.TRS(panel.position, panel.rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, size);
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
    }
}
