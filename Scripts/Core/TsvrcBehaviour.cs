using Tsvrc.Core.Compiled;
using Tsvrc.Utils;
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

        private UdonSharpBehaviour[] _eventListeners = new UdonSharpBehaviour[0];
        private string[] _eventKeys = new string[0];
        private string[] _eventCallbacks = new string[0];

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

        #region Events

        /// <summary>
        /// Registers <paramref name="listener"/> to receive a <see cref="UdonSharpBehaviour.SendCustomEvent"/> call
        /// with <paramref name="callbackName"/> as the method name whenever <paramref name="eventName"/> is emitted.
        /// Use <c>nameof()</c> for both parameters to avoid magic strings.
        /// </summary>
        public void TsSubscribe(UdonSharpBehaviour listener, string eventName, string callbackName)
        {
            _eventListeners = TsArray.Add(_eventListeners, new UdonSharpBehaviour[] { listener });
            _eventKeys = TsArray.Add(_eventKeys, new string[] { eventName });
            _eventCallbacks = TsArray.Add(_eventCallbacks, new string[] { callbackName });
        }

        /// <summary>
        /// Emits <paramref name="eventName"/> to all registered listeners via
        /// <see cref="UdonSharpBehaviour.SendCustomEvent"/>, invoking each listener's registered callback.
        /// </summary>
        public void TsEmit(string eventName)
        {
            UdonSharpBehaviour[] listeners = _eventListeners;
            string[] keys = _eventKeys;
            string[] callbacks = _eventCallbacks;
            int len = listeners.Length;
            for (int i = 0; i < len; i++)
                if (keys[i] == eventName)
                    listeners[i].SendCustomEvent(callbacks[i]);
        }

        #endregion

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
            _eventListeners = new UdonSharpBehaviour[0];
            _eventKeys = new string[0];
            _eventCallbacks = new string[0];
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