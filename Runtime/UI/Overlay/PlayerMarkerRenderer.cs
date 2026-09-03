using Tsvrc.Core;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.UI
{
    /// <summary>
    /// Pluggable strategy for presenting a single player's marker. <see cref="PlayerPositionOverlay"/>
    /// only reports world position - what a marker looks like and how it's drawn is entirely up
    /// to the subclass assigned to <see cref="PlayerPositionOverlay.Renderer"/>. See
    /// <see cref="RasterPlayerMarkerRenderer"/> for an optional "paint into a texture" base.
    /// Virtual with empty bodies, not <c>abstract</c>, so an unassigned or partial override
    /// never crashes.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PlayerMarkerRenderer : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        /// <summary>
        /// Whether <see cref="OnMarkerVisible"/> uses <c>headingDegrees</c>. Override to
        /// <c>false</c> to skip the overlay's <c>VRCPlayerApi.GetRotation()</c> call.
        /// </summary>
        public virtual bool UsesHeading => true;

        /// <summary>Called once per visible tracked player, every blink-show cycle.</summary>
        /// <param name="worldPosition">The player's current world position.</param>
        /// <param name="isLocalPlayer">True for the local player.</param>
        /// <param name="headingDegrees">Facing direction; 0 unless <see cref="UsesHeading"/>.</param>
        /// <param name="playerId"><see cref="Player.TsPlayer.GetPlayerID"/> format.</param>
        public virtual void OnMarkerVisible(Vector3 worldPosition, bool isLocalPlayer, float headingDegrees, string playerId)
        {
        }

        /// <summary>
        /// Called once per blink tick, after this cycle's <see cref="OnMarkerVisible"/> calls (a
        /// hide cycle has none). Batch-present anything accumulated this cycle here.
        /// </summary>
        public virtual void OnPresent()
        {
        }
    }
}
