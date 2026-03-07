using Tsvrc.Core.Compiled;
using UdonSharp;

namespace Tsvrc.Core
{
    /// <summary>
    /// Base class for all Tsvrc behaviours.
    /// Holds a reference to <see cref="Tsvrc.Core.Compiled.CompiledTsvrc"/> and exposes
    /// <see cref="TsConstruct"/> for dependency injection and <see cref="TsDestroy"/> for safe deferred destruction.
    /// </summary>
    public class TsvrcBehaviour : UdonSharpBehaviour
    {
        protected CompiledTsvrc _ts;

        private bool _isCreated = false;

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

        public void TsDestroy()
        {
            SendCustomEventDelayedFrames(nameof(_TsDestroyDelayed), 1);
        }

        public void _TsDestroyDelayed()
        {
            Destroy(gameObject);
        }

        #region Virtual Methods

        protected virtual void TsStart() { }

        #endregion
    }
}