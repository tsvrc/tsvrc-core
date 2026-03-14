#if UNITY_EDITOR

using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal abstract class Builder
    {
        internal abstract void BuildFields(CsWriter w, TsvrcConfig config, ScanResult result);
    }
}

#endif