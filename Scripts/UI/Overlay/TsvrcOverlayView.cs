using Tsvrc.Core;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.UI.Overlay
{
    /// <summary>
    /// Base class for all overlay views. Subclass this and override <see cref="_OnViewShow"/>
    /// and <see cref="_OnViewHide"/> to add per-view logic.
    /// Set <see cref="ViewName"/> in the Inspector (or use the GameObject name as a fallback).
    /// The compiler discovers this component automatically and generates a typed
    /// <c>_ts.Overlay.Show{ViewName}()</c> method.
    /// </summary>
    /// <remarks>
    /// Each concrete subclass must declare its own
    /// <c>[UdonBehaviourSyncMode(BehaviourSyncMode.None)]</c> (or the desired mode).
    /// UdonSharp does not inherit sync-mode attributes; omitting it gives the <c>Any</c> default,
    /// which shows a sync-mode dropdown in the Inspector and may generate Continuous traffic.
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TsvrcOverlayView : TsvrcBehaviour
    {
        [Header("Overlay View")]
        [Tooltip("Name used to generate _ts.Overlay.Show{ViewName}(). Falls back to the GameObject name if empty.")]
        public string ViewName;

        /// <summary>Called by the navigator just after this view's GameObject is activated.</summary>
        protected virtual void _OnViewShow() { }

        /// <summary>Called by the navigator just before this view's GameObject is deactivated.</summary>
        protected virtual void _OnViewHide() { }

        /// <summary>Activates this view. Called by <see cref="TsvrcOverlayNavigator"/>.</summary>
        public void ShowView()
        {
            gameObject.SetActive(true);
            _OnViewShow();
        }

        /// <summary>Deactivates this view. Called by <see cref="TsvrcOverlayNavigator"/>.</summary>
        public void HideView()
        {
            _OnViewHide();
            gameObject.SetActive(false);
        }
    }
}
