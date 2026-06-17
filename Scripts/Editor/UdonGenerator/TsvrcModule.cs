#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;

namespace Tsvrc.Editor.V2
{
    internal abstract class TsvrcModule
    {
        // Null when a module contributes no generated file of its own (e.g. it only wires data
        // into another module's class). WriteModules() skips writing in that case.
        internal virtual string FileName => null;
        internal virtual IEnumerable<string> WatchedAssets() => Enumerable.Empty<string>();

        internal abstract void LoadConfig();
        internal virtual string GenerateCode() => null;
        internal virtual bool AfterFilesStable() => false;
        internal virtual void Wire() { }
        internal virtual bool OnSceneHierarchyChanged() => false;

        // Returns field names this module will declare on TsvrcGenerated (via a partial).
        // Used by TsvrcGenerator to detect cross-module naming conflicts after LoadConfig().
        internal virtual IEnumerable<string> ExposedFieldNames() => Enumerable.Empty<string>();

        // Called with the set of conflicting names so the module can remove them and log errors.
        internal virtual void ExcludeFieldNames(IEnumerable<string> names) { }
    }
}
#endif
