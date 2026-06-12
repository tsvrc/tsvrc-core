#if UNITY_EDITOR
using UnityEngine;

namespace Tsvrc.Core
{
    // Consumed at compile time by TranslationModule (v2) — never included in the VRChat build.
    // Create via Assets/TsvrcGenerated or assign directly; one JSON file per language.
    public class TranslationConfig2 : ScriptableObject
    {
        [Tooltip("One JSON file per language. Each file must contain \"key\", \"label\", and \"entries\".")]
        public TextAsset[] LanguageFiles;
    }
}
#endif
