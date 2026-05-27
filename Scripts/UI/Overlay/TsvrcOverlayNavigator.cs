using Tsvrc.Core;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.UI.Overlay
{
    /// <summary>
    /// Manages which overlay view is visible and maintains a navigation back-stack.
    /// Extended by the generated <c>CompiledOverlay</c> class which adds typed
    /// <c>Show{ViewName}()</c> methods. Do not use this class directly in user code;
    /// interact via <c>_ts.Overlay</c>.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TsvrcOverlayNavigator : TsvrcBehaviour
    {
        [HideInInspector][SerializeField] public TsvrcOverlayView[] _views = new TsvrcOverlayView[0];
        [HideInInspector][SerializeField] public TsvrcOverlay _overlay;

        private int _currentViewIndex = -1;

        // Fixed-capacity back-stack (no allocations at runtime).
        private readonly int[] _viewStack = new int[16];
        private int _stackDepth = 0;

        public int CurrentViewIndex => _currentViewIndex;

        protected override void TsStart()
        {
            for (int i = 0; i < _views.Length; i++)
            {
                if (_views[i] == null) continue;
                // Give each view access to _ts so its callbacks can use the framework.
                _views[i].TsConstruct(this);
                // Use SetActive instead of HideView to skip the _OnViewHide callback during init.
                // Views have never been shown yet, so firing that callback here would be misleading.
                _views[i].gameObject.SetActive(false);
            }
            // Start with the container hidden; caller must call Open() explicitly.
            if (_overlay != null) _overlay.CloseOverlay();
        }


        /// <summary>Immediately switches to the view at <paramref name="index"/> and clears the back-stack.</summary>
        public void SetView(int index)
        {
            if (index < 0 || index >= _views.Length) return;
            if (index == _currentViewIndex) return;

            _stackDepth = 0; // clear the back-stack so Back() cannot return to a stale view
            HideCurrent();
            _currentViewIndex = index;
            ShowCurrent();
        }

        /// <summary>
        /// Pushes the current view onto the back-stack and switches to <paramref name="index"/>.
        /// Call <see cref="Back"/> to return.
        /// </summary>
        public void Push(int index)
        {
            if (index < 0 || index >= _views.Length) return;
            if (index == _currentViewIndex) return; // already showing this view, nothing to do
            // Only push a back-destination if there is an actual current view.
            // Pushing the no-view sentinel (-1) would cause Back() to show nothing.
            if (_stackDepth < _viewStack.Length && _currentViewIndex >= 0)
                _viewStack[_stackDepth++] = _currentViewIndex;

            HideCurrent();
            _currentViewIndex = index;
            ShowCurrent();
        }

        /// <summary>Returns to the previous view in the back-stack. No-op if the stack is empty.</summary>
        public void Back()
        {
            if (_stackDepth == 0) return;
            int prev = _viewStack[--_stackDepth];

            HideCurrent();
            _currentViewIndex = prev;
            ShowCurrent();
        }


        /// <summary>
        /// Makes the overlay container visible. If no view has been shown yet, shows view 0.
        /// </summary>
        public void Open()
        {
            if (_currentViewIndex < 0 && _views.Length > 0)
            {
                _currentViewIndex = 0;
                ShowCurrent();
            }
            if (_overlay != null) _overlay.OpenOverlay();
        }

        /// <summary>Hides the overlay container without changing the active view.</summary>
        public void Close()
        {
            if (_overlay != null) _overlay.CloseOverlay();
        }

        private void HideCurrent()
        {
            if (_currentViewIndex >= 0 && _currentViewIndex < _views.Length && _views[_currentViewIndex] != null)
                _views[_currentViewIndex].HideView();
        }

        private void ShowCurrent()
        {
            if (_currentViewIndex >= 0 && _currentViewIndex < _views.Length && _views[_currentViewIndex] != null)
                _views[_currentViewIndex].ShowView();
        }
    }
}
