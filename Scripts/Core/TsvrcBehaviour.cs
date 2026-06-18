using Tsvrc.Core.Compiled;
using Tsvrc.Core.Generated;
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
        protected TsvrcGenerated _ts;

        private UdonSharpBehaviour[] _subListeners = new UdonSharpBehaviour[0];
        private string[] _subKeys = new string[0];
        private string[] _subCallbacks = new string[0];

        /// <summary>
        /// Assigns the <see cref="TsvrcGenerated"/> reference and calls <see cref="TsStart"/>.
        /// </summary>
        public void TsConstruct(TsvrcGenerated tsvrc)
        {
            _ts = tsvrc;
            TsStart();
        }

        /// <summary>
        /// Propagates the <see cref="TsvrcGenerated"/> reference from an existing
        /// <see cref="TsvrcBehaviour"/> and fully constructs this behaviour.
        /// </summary>
        public void TsConstruct(TsvrcBehaviour parent)
        {
            TsConstruct(parent._ts);
        }

        /// <summary>
        /// Subscribes <paramref name="listener"/> to <paramref name="eventName"/> on this behaviour.
        /// Subscriptions are world-lifetime and never cleared. Use <c>nameof()</c> for both string parameters.
        /// </summary>
        public void TsSubscribe(UdonSharpBehaviour listener, string eventName, string callbackName)
        {
            _subListeners = TsArray.Add(_subListeners, new UdonSharpBehaviour[] { listener });
            _subKeys = TsArray.Add(_subKeys, new string[] { eventName });
            _subCallbacks = TsArray.Add(_subCallbacks, new string[] { callbackName });
        }

        /// <summary>
        /// Emits <paramref name="eventName"/> to all registered listeners.
        /// </summary>
        public void TsEmit(string eventName)
        {
            UdonSharpBehaviour[] listeners = _subListeners;
            string[] keys = _subKeys;
            string[] callbacks = _subCallbacks;
            int len = listeners.Length;
            for (int i = 0; i < len; i++)
                if (keys[i] == eventName)
                    listeners[i].SendCustomEvent(callbacks[i]);
        }

        /// <summary>
        /// Removes the GameObject from the scene.
        /// </summary>
        public virtual void TsDestroy()
        {
            Destroy(gameObject);
        }

        protected virtual void TsStart() { }
    }
}
