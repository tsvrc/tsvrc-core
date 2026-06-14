#if UNITY_EDITOR
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    public class TsvrcTranslationConfig : ScriptableObject
    {
        [Tooltip("One JSON file per language. Each file must contain \"key\", \"label\", and \"entries\".")]
        public TextAsset[] LanguageFiles;
    }
}
#endif
