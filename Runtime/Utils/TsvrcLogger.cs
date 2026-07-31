using Tsvrc.Core;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Utils
{
    /// <summary>
    /// Centralized logging sink. Access via <c>_ts.Log</c>, or prefer the
    /// <see cref="TsvrcBehaviour.LogInfo"/>/<see cref="TsvrcBehaviour.LogWarning"/>/<see cref="TsvrcBehaviour.LogError"/>
    /// wrappers, which supply the tag and context automatically.
    /// </summary>
    /// <remarks>
    /// Format: <c>[TsVRC] [prefix] [tag] message</c>. <c>TsVRC</c> (<see cref="FrameworkTag"/>) is
    /// fixed and internal-only, never configurable; <c>prefix</c> is each world's own optional
    /// <see cref="Prefix"/> (omitted when empty); <c>tag</c> is the logging class's name.
    /// Info/Warning/Error are each independently toggleable for Tsvrc Internal vs Your World
    /// (<see cref="TsvrcBehaviour.IsTsvrcInternal"/>), all six defaulting to <c>true</c> - configure
    /// via Tsvrc &gt; Configure &gt; Logging or directly on this component.
    /// </remarks>
    [TsWorldExtensionPoint("TsLogger")]
    public class TsvrcLogger : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        // Fixed, internal-only - identifies Tsvrc itself in every message. Also used directly
        // by TsvrcBehaviour's fallback path and TsJson, which have no Prefix to read.
        internal const string FrameworkTag = "TsVRC";

        [Tooltip("Optional project-specific tag shown after [TsVRC], e.g. \"SomeWorld\" -> \"[TsVRC] [SomeWorld] [ClassName] message\". Leave empty to omit.")]
        [SerializeField] private string _prefix = "";

        [Header("Tsvrc Internal")]
        [Tooltip("Shows/hides Info-level logs from the Tsvrc framework's own classes (TsvrcMemory, Process, etc.).")]
        [SerializeField] private bool _internalInfoEnabled = true;
        [Tooltip("Shows/hides Warning-level logs from the Tsvrc framework's own classes.")]
        [SerializeField] private bool _internalWarningEnabled = true;
        [Tooltip("Shows/hides Error-level logs from the Tsvrc framework's own classes.")]
        [SerializeField] private bool _internalErrorEnabled = true;

        [Header("Your World")]
        [Tooltip("Shows/hides Info-level logs from your own world's scripts.")]
        [SerializeField] private bool _worldInfoEnabled = true;
        [Tooltip("Shows/hides Warning-level logs from your own world's scripts.")]
        [SerializeField] private bool _worldWarningEnabled = true;
        [Tooltip("Shows/hides Error-level logs from your own world's scripts.")]
        [SerializeField] private bool _worldErrorEnabled = true;

        /// <summary>
        /// This project's optional tag, shown after the always-present <see cref="FrameworkTag"/>.
        /// Empty by default; set to your world/product's name if you want it in every log line.
        /// </summary>
        public string Prefix
        {
            get => _prefix;
            set => _prefix = value;
        }

        /// <summary>Shows/hides Info-level logs from the Tsvrc framework's own classes.</summary>
        public bool InternalInfoEnabled { get => _internalInfoEnabled; set => _internalInfoEnabled = value; }
        /// <summary>Shows/hides Warning-level logs from the Tsvrc framework's own classes.</summary>
        public bool InternalWarningEnabled { get => _internalWarningEnabled; set => _internalWarningEnabled = value; }
        /// <summary>Shows/hides Error-level logs from the Tsvrc framework's own classes.</summary>
        public bool InternalErrorEnabled { get => _internalErrorEnabled; set => _internalErrorEnabled = value; }

        /// <summary>Shows/hides Info-level logs from your own world's scripts.</summary>
        public bool WorldInfoEnabled { get => _worldInfoEnabled; set => _worldInfoEnabled = value; }
        /// <summary>Shows/hides Warning-level logs from your own world's scripts.</summary>
        public bool WorldWarningEnabled { get => _worldWarningEnabled; set => _worldWarningEnabled = value; }
        /// <summary>Shows/hides Error-level logs from your own world's scripts.</summary>
        public bool WorldErrorEnabled { get => _worldErrorEnabled; set => _worldErrorEnabled = value; }

        /// <summary>
        /// Logs an informational message. <paramref name="isInternal"/> selects which of
        /// <see cref="InternalInfoEnabled"/>/<see cref="WorldInfoEnabled"/> gates it; defaults to
        /// <c>false</c> (world) for direct calls not routed through a <see cref="TsvrcBehaviour"/>.
        /// </summary>
        public void Info(string tag, string message, bool isInternal = false, UdonSharpBehaviour context = null)
        {
            if (!(isInternal ? _internalInfoEnabled : _worldInfoEnabled)) return;
            Debug.Log(Format(_prefix, tag, message), context);
        }

        /// <summary>
        /// Logs a warning. <paramref name="isInternal"/> selects which of
        /// <see cref="InternalWarningEnabled"/>/<see cref="WorldWarningEnabled"/> gates it;
        /// defaults to <c>false</c> (world) for direct calls not routed through a <see cref="TsvrcBehaviour"/>.
        /// </summary>
        public void Warning(string tag, string message, bool isInternal = false, UdonSharpBehaviour context = null)
        {
            if (!(isInternal ? _internalWarningEnabled : _worldWarningEnabled)) return;
            Debug.LogWarning(Format(_prefix, tag, message), context);
        }

        /// <summary>
        /// Logs an error. <paramref name="isInternal"/> selects which of
        /// <see cref="InternalErrorEnabled"/>/<see cref="WorldErrorEnabled"/> gates it; defaults
        /// to <c>false</c> (world) for direct calls not routed through a <see cref="TsvrcBehaviour"/>.
        /// </summary>
        public void Error(string tag, string message, bool isInternal = false, UdonSharpBehaviour context = null)
        {
            if (!(isInternal ? _internalErrorEnabled : _worldErrorEnabled)) return;
            Debug.LogError(Format(_prefix, tag, message), context);
        }

        // Internal so TsvrcBehaviour's fallback path can format identically without duplicating the format string.
        // prefix may be null/empty - the optional project tag is simply omitted in that case.
        internal static string Format(string prefix, string tag, string message) =>
            string.IsNullOrEmpty(prefix)
                ? $"[{FrameworkTag}] [{tag}] {message}"
                : $"[{FrameworkTag}] [{prefix}] [{tag}] {message}";
    }
}
