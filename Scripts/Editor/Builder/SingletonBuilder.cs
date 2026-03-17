#if UNITY_EDITOR

using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal class SingletonBuilder : Builder
    {
        internal override void BuildFields(CsWriter w, TsvrcConfig config, ScanResult result)
        {
            w.Region("Singletons");
            foreach (var field in result.Fields)
            {
                if (field.CallSites.Count == 0)
                {
                    w.Line($"[HideInInspector] public {field.Type} {field.Name} {{ get; private set; }}");
                    continue;
                }
                w.Line($"[SerializeField] public {field.Type} {field.Name};");
            }
            w.EndRegion();
        }
    }
}

#endif