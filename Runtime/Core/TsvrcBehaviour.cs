using Tsvrc.Core.Generated;
using Tsvrc.Utils;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    /// <summary>
    /// An enhanced <see cref="UdonSharpBehaviour"/> with structured initialization
    /// and dependency injection.
    /// </summary>
    [TsWorldExtensionPoint("TsBehaviour")]
    public class TsvrcBehaviour : UdonSharpBehaviour
    {
        protected TsRoot _ts;

        private bool _isConstructed;

        private UdonSharpBehaviour[] _subListeners = new UdonSharpBehaviour[0];
        private string[] _subKeys = new string[0];
        private string[] _subCallbacks = new string[0];

        // Logical subscriber count. The three backing arrays above are capacity, not
        // count: TsSubscribe grows them by doubling for amortized O(1) growth. TsEmit
        // iterates up to this count, not the arrays' Length, so unused trailing
        // capacity is never touched.
        private int _subCount;

        /// <summary>
        /// Assigns the <see cref="TsRoot"/> reference and calls <see cref="TsStart"/>.
        /// A no-op on every call after the first: construction runs exactly once per
        /// instance, so a subclass's <see cref="TsStart"/> can safely do non-idempotent
        /// one-time setup (subscribing, starting timers, etc.) without guarding against
        /// being re-entered by an accidental second TsConstruct call.
        /// </summary>
        public void TsConstruct(TsRoot tsvrc)
        {
            if (_isConstructed) return;
            _isConstructed = true;

            _ts = tsvrc;
            TsStart();
        }

        /// <summary>
        /// Propagates the <see cref="TsRoot"/> reference from an existing
        /// <see cref="TsvrcBehaviour"/> and fully constructs this behaviour. A no-op on
        /// every call after the first, same guarantee as <see cref="TsConstruct(TsRoot)"/>:
        /// checked before <paramref name="parent"/> is ever dereferenced, so a second call
        /// can't throw even if <paramref name="parent"/> is null on that later call.
        /// </summary>
        public void TsConstruct(TsvrcBehaviour parent)
        {
            if (_isConstructed) return;
            TsConstruct(parent._ts);
        }

        /// <summary>
        /// Subscribes <paramref name="listener"/> to <paramref name="eventName"/> on this behaviour.
        /// Subscriptions are world-lifetime and never cleared. Use <c>nameof()</c> for both string parameters.
        /// </summary>
        public void TsSubscribe(UdonSharpBehaviour listener, string eventName, string callbackName)
        {
            if (_subCount == _subListeners.Length)
            {
                int newCapacity = _subCount == 0 ? 4 : _subCount * 2;
                _subListeners = GrowBehaviours(_subListeners, newCapacity);
                _subKeys = GrowStrings(_subKeys, newCapacity);
                _subCallbacks = GrowStrings(_subCallbacks, newCapacity);
            }

            _subListeners[_subCount] = listener;
            _subKeys[_subCount] = eventName;
            _subCallbacks[_subCount] = callbackName;
            _subCount++;
        }

        private static UdonSharpBehaviour[] GrowBehaviours(UdonSharpBehaviour[] original, int newCapacity)
        {
            UdonSharpBehaviour[] result = new UdonSharpBehaviour[newCapacity];
            System.Array.Copy(original, result, original.Length);
            return result;
        }

        private static string[] GrowStrings(string[] original, int newCapacity)
        {
            string[] result = new string[newCapacity];
            System.Array.Copy(original, result, original.Length);
            return result;
        }

        /// <summary>
        /// Emits <paramref name="eventName"/> to all registered listeners.
        /// </summary>
        public void TsEmit(string eventName)
        {
            UdonSharpBehaviour[] listeners = _subListeners;
            string[] keys = _subKeys;
            string[] callbacks = _subCallbacks;
            // Snapshotting _subCount here (a value type) means a reentrant TsSubscribe
            // during this loop's iteration, which either writes into unused trailing
            // capacity in the same array or, on grow, replaces the fields with new
            // array instances, can never become visible within this TsEmit call.
            int len = _subCount;
            for (int i = 0; i < len; i++)
            {
                if (keys[i] != eventName) continue;

                UdonSharpBehaviour listener = listeners[i];

                // A listener that's truly null throws below. A bad subscription is a
                // programmer error that should fail loudly. A listener whose GameObject
                // was destroyed after subscribing is different: it was valid at
                // subscribe time, so it's skipped instead of invoking a callback on a
                // dead object. Unity's overridden `==` can't tell the two cases apart on
                // its own; casting to object first bypasses the override for a true
                // reference-null check, isolating the "destroyed" case.
                if ((object)listener != null && listener == null) continue;

                listener.SendCustomEvent(callbacks[i]);
            }
        }

        /// <summary>
        /// Removes the GameObject from the scene.
        /// </summary>
        public virtual void TsDestroy()
        {
            Destroy(gameObject);
        }

        /// <summary>
        /// <c>true</c> if this class belongs to the Tsvrc framework itself, <c>false</c> for a
        /// world's own scripts. Overridden to <c>true</c> once per framework base class (e.g.
        /// <see cref="Process"/>, <see cref="TsvrcMemory"/>) so every subclass inherits the
        /// correct classification automatically. Drives the Log*() methods below, which pass
        /// this to <c>_ts.Log</c> so the "Tsvrc Internal" and "Your World" log levels configured
        /// in Tsvrc &gt; Configure &gt; Logging can be shown/hidden independently.
        /// </summary>
        protected virtual bool IsTsvrcInternal => false;

        /// <summary>
        /// Logs an informational message via <c>_ts.Log</c>, tagged with this behaviour's
        /// class name (via <see cref="UdonSharpBehaviour.GetUdonTypeName"/>) and this instance
        /// as the click-to-select console context. No-op when Info logging is disabled for
        /// this behaviour's <see cref="IsTsvrcInternal"/> category. Falls back to
        /// <see cref="Debug.Log(object, UnityEngine.Object)"/> directly, with the same tag and
        /// no level filtering, if called before <see cref="TsConstruct(TsRoot)"/> has wired up <c>_ts</c>.
        /// </summary>
        protected void LogInfo(string message)
        {
            TsvrcLogger log = _ts != null ? _ts.Log : null;
            if (log != null) { log.Info(GetUdonTypeName(), message, IsTsvrcInternal, this); return; }
            Debug.Log(TsvrcLogger.Format(string.Empty, GetUdonTypeName(), message), this);
        }

        /// <summary>
        /// Logs a warning via <c>_ts.Log</c>, tagged with this behaviour's class name (via
        /// <see cref="UdonSharpBehaviour.GetUdonTypeName"/>) and this instance as the
        /// click-to-select console context. No-op when Warning logging is disabled for this
        /// behaviour's <see cref="IsTsvrcInternal"/> category. Falls back to
        /// <see cref="Debug.LogWarning(object, UnityEngine.Object)"/> directly, with the same tag
        /// and no level filtering, if called before <see cref="TsConstruct(TsRoot)"/> has wired up <c>_ts</c>.
        /// </summary>
        protected void LogWarning(string message)
        {
            TsvrcLogger log = _ts != null ? _ts.Log : null;
            if (log != null) { log.Warning(GetUdonTypeName(), message, IsTsvrcInternal, this); return; }
            Debug.LogWarning(TsvrcLogger.Format(string.Empty, GetUdonTypeName(), message), this);
        }

        /// <summary>
        /// Logs an error via <c>_ts.Log</c>, tagged with this behaviour's class name (via
        /// <see cref="UdonSharpBehaviour.GetUdonTypeName"/>) and this instance as the
        /// click-to-select console context. No-op when Error logging is disabled for this
        /// behaviour's <see cref="IsTsvrcInternal"/> category. Falls back to
        /// <see cref="Debug.LogError(object, UnityEngine.Object)"/> directly, with the same tag
        /// and no level filtering, if called before <see cref="TsConstruct(TsRoot)"/> has wired up <c>_ts</c>.
        /// </summary>
        protected void LogError(string message)
        {
            TsvrcLogger log = _ts != null ? _ts.Log : null;
            if (log != null) { log.Error(GetUdonTypeName(), message, IsTsvrcInternal, this); return; }
            Debug.LogError(TsvrcLogger.Format(string.Empty, GetUdonTypeName(), message), this);
        }

        protected virtual void TsStart() { }
    }
}
