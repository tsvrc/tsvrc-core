#if UNITY_EDITOR
using UnityEditor;

namespace Tsvrc.Editor
{
    // delayCall defers until scene objects are registered; FindObjectsOfType is empty during the static constructor.
    [InitializeOnLoad]
    internal static class TsDomainReloadHandler
    {
        static TsDomainReloadHandler() => EditorApplication.delayCall += () => TsGenerator.AfterDomainReload();
    }
}
#endif
