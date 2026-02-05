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
        }
    }
}
