#if UNITY_EDITOR

using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal class PoolBuilder : Builder
    {
        internal override void BuildFields(CsWriter w, TsvrcConfig config, ScanResult result)
        {
            w.Region("Pools");
            foreach (var field in result.Fields)
                w.Line($"[SerializeField] private {field.Type} _{field.Name};");
            w.EndRegion();
        }
    }
}

#endif