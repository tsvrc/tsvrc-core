using Tsvrc.Core.Compiled;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    /// <summary>
    /// An enhanced <see cref="UdonSharpBehaviour"/> with structured construction,
    /// dependency injection, and pooled reuse.
    /// <see cref="TsConstruct(CompiledTsvrc)"/> must be called to initialize Tsvrc
    /// functionalities. Behaviours placed directly in the scene are constructed
    /// automatically by the compiler; behaviours spawned at runtime can be constructed
    /// from an existing <see cref="TsvrcBehaviour"/> via
    /// <see cref="TsConstruct(TsvrcBehaviour)"/>.
    /// </summary>
    public class TsvrcBehaviour : UdonSharpBehaviour
    {
        protected CompiledTsvrc _ts;

        private bool _isConstructed = false;
        public bool IsConstructed => _isConstructed;

        /// <summary>
        /// Constructs this behaviour with the given <see cref="CompiledTsvrc"/>.
        /// Called automatically by the compiler for behaviours placed in the scene.
        /// </summary>
        public void TsConstruct(CompiledTsvrc tsvrc)
        {
            if (_isConstructed)
            {
                Debug.LogError($"[CompiledTsvrc] {gameObject.name}: TsConstruct called on an already constructed instance.");
                return;
            }

            _isConstructed = true;
            _ts = tsvrc;
            TsStart();
        }

        /// <summary>
        /// Constructs this behaviour by propagating the <see cref="CompiledTsvrc"/> from
        /// an existing <see cref="TsvrcBehaviour"/>.
        /// </summary>
        public void TsConstruct(TsvrcBehaviour parent)
        {
            TsConstruct(parent._ts);
        }

        #region Virtual Lifecycle

        /// <summary>
        /// Resets this behaviour to a clean state without destroying it, equivalent to a
        /// new instance. Deactivates the GameObject and clears the constructed state so
        /// <see cref="TsConstruct(CompiledTsvrc)"/> can be called again.
        /// The GameObject remains in the scene so its VRChat network identity is preserved.
        /// </summary>
        public virtual void TsRelease()
        {
            _isConstructed = false;
        }

        /// <summary>
        /// Destroys the GameObject, permanently removing it and freeing its position in
        /// memory. Use this when the behaviour should no longer exist in the scene at all,
        /// as opposed to <see cref="TsRelease"/> which keeps it for reuse.
        /// </summary>
        public virtual void TsDestroy()
        {
            Destroy(gameObject);
        }

        protected virtual void TsStart() { }

        #endregion
    }
}