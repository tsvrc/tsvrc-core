#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    internal class ScanResult
    {
        internal SortedSet<string> Namespaces = new SortedSet<string>();
        internal SortedSet<string> Types = new SortedSet<string>();
        internal Builder Builder;
    }
}
#endif