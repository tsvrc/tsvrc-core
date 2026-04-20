#if UNITY_EDITOR
using UnityEngine;

namespace Tsvrc.Editor
{
    internal class TsvrcField
    {
        internal string Type;
        internal string Name;
        internal string Namespace;
        // The original scene object from config, used by the wirer to assign SerializedProperty values.
        internal Object SourceObject;
        // Number of pool slots to generate. Always 1 for singletons and constructs.
        internal int SlotCount = 1;
        // Number of source-level call sites found for this field. Only populated for singletons and factories.
        internal int CallSiteCount;
        // When set, the wirer assigns this field unconditionally without checking call sites.
        // Used for fields that already existed in the last compiled output.
        internal bool WireAlways;
    }
}
#endif
