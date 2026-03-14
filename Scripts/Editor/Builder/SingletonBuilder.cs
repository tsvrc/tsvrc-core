#if UNITY_EDITOR

using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal class SingletonBuilder : Builder
    {
        internal override void BuildFields(CsWriter w, TsvrcConfig config, ScanResult result)
        {
            w.Region("Singletons");
            foreach (var type in result.Types)
                w.Line($"[SerializeField] public {type} _{type.ToLower()};");
            w.EndRegion();
        }
    }
}

#endif