#if UNITY_EDITOR
using UnityEngine;

namespace Tsvrc.Editor
{
    public class TsTranslationConfig : ScriptableObject
    {
        [Tooltip("One JSON file per language. Each file must contain \"key\", \"label\", and \"entries\".")]
        public TextAsset[] LanguageFiles;
    }
}
#endif
