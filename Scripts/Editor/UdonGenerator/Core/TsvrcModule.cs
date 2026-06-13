#if UNITY_EDITOR
using UnityEditor;

namespace Tsvrc.Editor.V2
{
    internal abstract class TsvrcModule
    {
        internal virtual string FileName => null;
        internal virtual string StartMethodCall => null;

        internal virtual void OnDomainReloaded() { }
        internal virtual string GenerateCode() => string.Empty;
        internal virtual void AfterFilesStable() { }
        internal virtual void Wire(SerializedObject target) { }
        internal virtual void OnSceneHierarchyChanged() { }
    }
}
#endif
