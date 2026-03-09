using Tsvrc.Core.Compiled;
using UdonSharp;

namespace Tsvrc.Core
{
    /// <summary>
    /// Base class for all Tsvrc behaviours.
    /// Holds a reference to <see cref="Tsvrc.Core.Compiled.CompiledTsvrc"/> and exposes
    /// <see cref="TsConstruct"/> for dependency injection and <see cref="TsRelease"/> for returning a slot to the pool.
    /// </summary>
    public class TsvrcBehaviour : UdonSharpBehaviour
    {
        protected CompiledTsvrc _ts;

        private bool _isCreated = false;
        public bool IsCreated => _isCreated;

        public void TsConstruct(CompiledTsvrc tsvrc)
        {
            if (_isCreated) return;

            _isCreated = true;
            _ts = tsvrc;
            TsStart();
        }

        public void TsConstruct(TsvrcBehaviour parent)
        {
            if (_isCreated) return;

            _isCreated = true;
            _ts = parent._ts;
            TsStart();
        }

        // Releases this behaviour back to its pool: deactivates the GameObject and resets
        // the constructed state so TsConstruct() can be called again on the next Get.
        // The GameObject stays in the scene so its VRChat network ID is preserved.
        public void TsRelease()
        {
            SendCustomEventDelayedFrames(nameof(_TsReleaseDelayed), 1);
        }

        public void _TsReleaseDelayed()
        {
            _isCreated = false;
            gameObject.SetActive(false);
        }

        #region Virtual Methods

        protected virtual void TsStart() { }

        #endregion
    }
}