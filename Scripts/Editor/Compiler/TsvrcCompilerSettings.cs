#if UNITY_EDITOR
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Controls validation behavior across all Tsvrc compiler modules.
    /// Loaded from Assets/Tsvrc/Resources/CompilerSettings.asset
    /// </summary>
    public enum ValidationLevel
    {
        /// <summary>Validation failure stops compilation with an error.</summary>
        Error = 0,

        /// <summary>Validation failure logs a warning but compilation continues.</summary>
        Warn = 1,

        /// <summary>Validation is skipped silently.</summary>
        Silent = 2,
    }

    /// <summary>
    /// Global compiler settings for validation behavior.
    /// Accessible via TsvrcCompilerSettings.Instance
    /// </summary>
    public class TsvrcCompilerSettings : ScriptableObject
    {
        [Header("Pool Validation")]
        [Tooltip("How strictly to validate [TsWirePool] declarations against TsvrcConfig registrations.")]
        public ValidationLevel PoolValidation = ValidationLevel.Error;

        [Header("Future Module Validation")]
        [Tooltip("Validation strictness for singleton declarations (planned feature).")]
        public ValidationLevel SingletonValidation = ValidationLevel.Warn;

        [Tooltip("Validation strictness for factory declarations (planned feature).")]
        public ValidationLevel FactoryValidation = ValidationLevel.Warn;

        [Tooltip("Validation strictness for construct declarations (planned feature).")]
        public ValidationLevel ConstructValidation = ValidationLevel.Warn;

        private static TsvrcCompilerSettings _instance;

        /// <summary>
        /// Loads the compiler settings from Assets/Tsvrc/Resources/CompilerSettings.asset
        /// If not found, returns a default instance with sensible defaults.
        /// </summary>
        public static TsvrcCompilerSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Resources.Load<TsvrcCompilerSettings>("CompilerSettings");
                    if (_instance == null)
                    {
                        _instance = CreateInstance<TsvrcCompilerSettings>();
                        Debug.LogWarning(
                            "[TsvrcCompilerSettings] CompilerSettings not found in Resources/CompilerSettings.asset. " +
                            "Using default validation levels. Create one via Tsvrc > Compiler Settings for persistent configuration.");
                    }
                }
                return _instance;
            }
        }
    }
}
#endif
