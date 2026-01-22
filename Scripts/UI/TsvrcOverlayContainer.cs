
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Tsvrc.UI
{
    public class TsvrcOverlayContainer : UdonSharpBehaviour
    {
        public Canvas canvas;
        public Material overlayMaterial;

        #region Unity Callbacks

        protected void Start()
        {
            if (overlayMaterial != null)
            {
                // Set material on all MeshRenderers in child objects
                MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
                foreach (MeshRenderer renderer in renderers)
                {
                    renderer.material = overlayMaterial;
                }

                // Set material on all UI Images in child objects
                Image[] images = GetComponentsInChildren<Image>(true);
                foreach (Image image in images)
                {
                    image.material = overlayMaterial;
                }

                // Set material on all UI RawImages in child objects
                RawImage[] rawImages = GetComponentsInChildren<RawImage>(true);
                foreach (RawImage rawImage in rawImages)
                {
                    rawImage.material = overlayMaterial;
                }
            }
        }

        protected void Update()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            // Get player head position and rotation
            Vector3 headPosition = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Quaternion headRotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;

            gameObject.transform.position = headPosition + headRotation * Vector3.forward;
            gameObject.transform.rotation = headRotation;
        }

        #endregion
    }
}
