using Tsvrc.Core;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.UI.Overlay
{
    public class TsvrcOverlay : TsvrcBehaviour
    {
        public Transform PlayerHeadPosition;
        public GameObject Container;
        public Transform DesktopDisplay;
        public Vector3 DesktopDisplayScale;

        // Getters for VRDisplay with calculations
        public Vector3 VRDisplayScale
        {
            get
            {
                Vector3 scale = DesktopDisplayScale;
                scale.x *= 0.3f;
                scale.y *= 0.3f;
                return scale;
            }
        }

        public Vector3 VRDisplayLocalPosition
        {
            get
            {
                if (PlayerHeadPosition == null) return Vector3.zero;
                Vector3 calculatedScale = VRDisplayScale;
                Vector3 position = PlayerHeadPosition.localPosition;
                position.z += 0.5f;
                position.y -= calculatedScale.y * 0.35f;
                return position;
            }
        }

        public Quaternion VRDisplayRotation
        {
            get
            {
                if (DesktopDisplay == null) return Quaternion.identity;
                return DesktopDisplay.localRotation * Quaternion.Euler(15f, 0f, 0f);
            }
        }

        private void Start()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && localPlayer.IsUserInVR() && DesktopDisplay != null)
            {
                DesktopDisplay.localScale = VRDisplayScale;
                DesktopDisplay.localPosition = VRDisplayLocalPosition;
                DesktopDisplay.localRotation = VRDisplayRotation;
            }
        }

        private void Update()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            Vector3 headPosition = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Quaternion headRotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;

            Container.transform.position = headPosition - headRotation * PlayerHeadPosition.localPosition;
            Container.transform.rotation = headRotation;
        }

        private void OnDrawGizmos()
        {
            if (Container != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(Container.transform.position, Container.transform.lossyScale);
            }

            if (PlayerHeadPosition != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawSphere(PlayerHeadPosition.position, 0.05f);
            }

            if (DesktopDisplay != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(DesktopDisplay.position, DesktopDisplay.lossyScale);
            }

            // Draw calculated VRDisplay position and scale
            if (DesktopDisplay != null && PlayerHeadPosition != null && Container != null)
            {
                Gizmos.color = Color.yellow;
                Vector3 worldPos = Container.transform.TransformPoint(VRDisplayLocalPosition);
                Quaternion worldRot = Container.transform.rotation * VRDisplayRotation;
                
                Gizmos.matrix = Matrix4x4.TRS(worldPos, worldRot, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, VRDisplayScale);
                Gizmos.matrix = Matrix4x4.identity;
            }
        }
    }
}
