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
    //
    // Stores the scene asset's GUID, not its path: Unity keeps a GUID's .meta mapping correct
    // automatically across a rename or move, so resolving the current path from the GUID at read
    // time makes a rename/move a non-issue instead of a silent, permanent break. A GUID that no
    // longer resolves to any path at all means the scene asset itself was deleted, not just
    // moved; see TsLinkedScene.IsConfiguredButMissing for how that's distinguished from "exists,
    // just isn't currently open".
    internal class TsLinkedSceneConfig : ScriptableObject
    {
        public string SceneGuid;
    }
}
#endif
