#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tsvrc.Editor
{
    internal class ScanResult
    {
        internal HashSet<TsvrcField> Fields = new HashSet<TsvrcField>();
        internal Builder Builder;
    }
}
#endif