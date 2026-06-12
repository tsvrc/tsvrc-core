#if UNITY_EDITOR
using UnityEditor;

namespace Tsvrc.Editor.V2
{
    // [InitializeOnLoad] fires on every domain reload, even when no assets changed.
    // AssetPostprocessor.OnPostprocessAllAssets with didDomainReload only fires when
    // there are also pending asset changes in the same cycle, making it unreliable here.
    [InitializeOnLoad]
    internal static class TsvrcDomainReloadHandler
    {
        static TsvrcDomainReloadHandler() => TsvrcGenerator.AfterDomainReload();
    }
}
#endif
