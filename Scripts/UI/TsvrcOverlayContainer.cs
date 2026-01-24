using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.UI
{
    public class TsvrcOverlayContainer : UdonSharpBehaviour
    {
        public Canvas canvas;

        #region Unity Callbacks

        protected void Start()
        {

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
