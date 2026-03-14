#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    internal class ScanResult
    {
        public SortedSet<string> Namespaces = new SortedSet<string>();
    }
}
#endif