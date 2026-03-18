#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal class TsvrcField
    {
        internal string Type;
        internal string Name;
        internal string Namespace;
        /// <summary>The original scene object from config (used by the wirer to assign SerializedProperty values).</summary>
        internal Object SourceObject;
        /// <summary>Number of pool slots to generate. Always 1 for singletons and constructs.</summary>
        internal int SlotCount = 1;
        internal List<TsvrcCallSite> CallSites = new List<TsvrcCallSite>();
    }
}
#endif
