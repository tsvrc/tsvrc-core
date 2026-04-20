#if UNITY_EDITOR
using UnityEngine;

namespace Tsvrc.Core
{
    // Do not create manually, use Tsvrc > Localization.
    // Consumed at compile time by TranslationModule and never included in the VRChat build.
    public class TranslationConfig : ScriptableObject
    {
        [Tooltip("One JSON file per language. Each file must contain the fields \"key\", \"label\", and \"entries\".")]
        public TextAsset[] LanguageFiles;
    }
}
#endif
