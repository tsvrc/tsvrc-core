#if UNITY_EDITOR
using UnityEngine;

namespace Tsvrc.Editor
{
    // Persisted, per-consuming-project pointer to the one scene TsGenerator is allowed to read
    // scene-sourced config from (TsConfig, [WirePool] scans, the compiled scaffold root). Lives
    // under TsPaths.GeneratedFolder (the consuming project's own generated output), not the
    // tsvrc package itself: which scene a world's generated code is tied to is project
    // configuration, not a tsvrc library default the way TsBuiltinConfig.asset is. See
    // TsLinkedScene for how this is read and written, and why it exists.
    internal class TsLinkedSceneConfig : ScriptableObject
    {
        public string ScenePath;
    }
}
#endif
