#if UNITY_EDITOR
using UnityEditor;

namespace Tsvrc.Editor.V2
{
    // delayCall defers until scene objects are registered; FindObjectsOfType is empty during the static constructor.
    [InitializeOnLoad]
    internal static class TsvrcDomainReloadHandler
    {
        static TsvrcDomainReloadHandler() => EditorApplication.delayCall += () => TsvrcGenerator.AfterDomainReload();
    }
}
#endif
