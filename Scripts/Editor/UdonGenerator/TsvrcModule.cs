#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

namespace Tsvrc.Editor.V2
{
    internal abstract class TsvrcModule
    {
        internal abstract string FileName { get; }
        internal virtual IEnumerable<string> WatchedAssets() => Enumerable.Empty<string>();

        internal abstract void LoadConfig();
        internal abstract string GenerateCode();
        internal virtual bool AfterFilesStable() => false;
        internal virtual void Wire(Scene scene) { }
        internal virtual bool OnSceneHierarchyChanged() => false;
    }
}
#endif
