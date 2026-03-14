#if UNITY_EDITOR
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal abstract class Scanner
    {
        internal abstract ScanResult Scan(TsvrcConfig config);
    }
}
#endif