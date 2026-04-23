using Tsvrc.Core.Compiled;
using Tsvrc.Utils;
using UdonSharp;

namespace Tsvrc.Core
{
    /// <summary>
    /// An enhanced <see cref="UdonSharpBehaviour"/> with structured initialization
    /// and dependency injection.
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
            _ts = tsvrc;
            TsStart();
        }

        /// <summary>
        /// Propagates the <see cref="CompiledTsvrc"/> reference from an existing
        /// <see cref="TsvrcBehaviour"/> and fully constructs this behaviour.
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

        #region Lifecycle

        /// <summary>
        /// Destroys the GameObject, permanently removing it and freeing its position in memory.
        /// </summary>
        public virtual void TsDestroy()
        {
            Destroy(gameObject);
        }

        protected virtual void TsStart() { }

        #endregion
    }
}
