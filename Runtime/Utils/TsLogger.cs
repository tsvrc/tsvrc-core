using Tsvrc.Core;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Utils
{
    /// <summary>
    /// Centralized logging sink. Access via <c>_ts.Log</c>, or prefer the
    /// <see cref="TsBehaviour.LogInfo"/>/<see cref="TsBehaviour.LogWarning"/>/<see cref="TsBehaviour.LogError"/>
    /// wrappers, which supply the tag and context automatically.
    /// </summary>
    /// <remarks>
    /// Every message is formatted as <c>[tag] message</c>. <b>Warning and Error always fire</b> -
    /// only <see cref="Info"/> is gated by <see cref="InfoEnabled"/>, matching the convention that
    /// diagnostics you can silence are never the ones that indicate something actually went wrong.
    /// The <see cref="InfoEnabled"/> check runs before any string formatting, so a disabled Info
    /// call costs a single branch, never an allocation.
    /// </remarks>
    public class TsLogger : TsBehaviour
    {
        [Tooltip("When disabled, Info() calls are skipped entirely (no formatting, no Debug.Log). Warning and Error are never gated.")]
        [SerializeField] private bool _infoEnabled = true;

        /// <summary>Enables or disables <see cref="Info"/> logging at runtime.</summary>
        public bool InfoEnabled
        {
            get => _infoEnabled;
            set => _infoEnabled = value;
        }

        /// <summary>Logs an informational message. No-op when <see cref="InfoEnabled"/> is <c>false</c>.</summary>
        public void Info(string tag, string message, UdonSharpBehaviour context = null)
        {
            if (!_infoEnabled) return;
            Debug.Log(Format(tag, message), context);
        }

        /// <summary>Logs a warning. Always fires, regardless of <see cref="InfoEnabled"/>.</summary>
        public void Warning(string tag, string message, UdonSharpBehaviour context = null)
        {
            Debug.LogWarning(Format(tag, message), context);
        }

        /// <summary>Logs an error. Always fires, regardless of <see cref="InfoEnabled"/>.</summary>
        public void Error(string tag, string message, UdonSharpBehaviour context = null)
        {
            Debug.LogError(Format(tag, message), context);
        }

        // Internal so TsBehaviour's fallback path (used when _ts/_ts.Log is not yet wired,
        // e.g. before TsConstruct) can format identically without duplicating the format string.
        internal static string Format(string tag, string message) => $"[{tag}] {message}";
    }
}
