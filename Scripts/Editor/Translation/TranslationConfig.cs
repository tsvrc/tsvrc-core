#if UNITY_EDITOR
using UnityEngine;

namespace Tsvrc.Core
{
    /// <summary>
    /// Editor-only config asset for the Tsvrc Translation system.
    /// Create via Tsvrc > Translation — do not create manually.
    /// Consumed at compile time by TranslationModule; never included in the VRChat build.
    /// </summary>
    public class TranslationConfig : ScriptableObject
    {
        [Tooltip("One JSON file per language. Each file must contain the fields \"key\", \"label\", and \"entries\".")]
        public TextAsset[] LanguageFiles;
    }
}
#endif
