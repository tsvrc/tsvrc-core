using System.Collections.Generic;
using Tsvrc.UI;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Records every OnMarkerVisible/OnPresent call's args so PlayerPositionOverlay's dispatch
    // can be asserted independent of any actual drawing/projection logic.
    public class RecordingPlayerMarkerRenderer : PlayerMarkerRenderer
    {
        public bool UsesHeadingValue = true;
        public override bool UsesHeading => UsesHeadingValue;

        public int OnMarkerVisibleCount;
        public int OnPresentCount;

        public readonly List<Vector3> WorldPositions = new List<Vector3>();
        public readonly List<bool> IsLocalPlayerArgs = new List<bool>();
        public readonly List<float> HeadingArgs = new List<float>();
        public readonly List<string> PlayerIdArgs = new List<string>();

        public override void OnMarkerVisible(Vector3 worldPosition, bool isLocalPlayer, float headingDegrees, string playerId)
        {
            OnMarkerVisibleCount++;
            WorldPositions.Add(worldPosition);
            IsLocalPlayerArgs.Add(isLocalPlayer);
            HeadingArgs.Add(headingDegrees);
            PlayerIdArgs.Add(playerId);
        }

        public override void OnPresent()
        {
            OnPresentCount++;
        }
    }

    // Records every DrawMarker call's args so RasterPlayerMarkerRenderer's projection and
    // dispatch can be asserted independent of TextureGraphics2D's own pixel-drawing logic.
    public class RecordingRasterPlayerMarkerRenderer : RasterPlayerMarkerRenderer
    {
        public int DrawMarkerCount;
        public readonly List<int> PixelXArgs = new List<int>();
        public readonly List<int> PixelYArgs = new List<int>();
        public readonly List<bool> IsLocalPlayerArgs = new List<bool>();
        public readonly List<float> HeadingArgs = new List<float>();
        public readonly List<string> PlayerIdArgs = new List<string>();

        public override void DrawMarker(Color32[] pixelBuffer, int textureWidth, int textureHeight,
            int pixelX, int pixelY, bool isLocalPlayer, float headingDegrees, string playerId)
        {
            DrawMarkerCount++;
            PixelXArgs.Add(pixelX);
            PixelYArgs.Add(pixelY);
            IsLocalPlayerArgs.Add(isLocalPlayer);
            HeadingArgs.Add(headingDegrees);
            PlayerIdArgs.Add(playerId);
        }
    }
}
