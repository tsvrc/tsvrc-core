using Tsvrc.Core.Compiled;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    public class TsvrcBehaviour : UdonSharpBehaviour
    {
        protected CompiledTsvrc _ts;

        private bool _isCreated = false;

        public void TsConstruct(CompiledTsvrc tsvrc)
        {
            if (_isCreated)
            {
                Debug.LogWarning($"[TsvrcBehaviour] {name} is already constructed. Ignoring duplicate construction.");
                return;
            }

            _isCreated = true;
            _ts = tsvrc;
            TsStart();
        }

        /// <summary>
        /// Constructs this behavious from another behaviour.
        /// </summary>
        public void TsConstruct(TsvrcBehaviour behaviour)
        {
            TsConstruct(behaviour._ts);
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