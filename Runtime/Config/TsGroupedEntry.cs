using System;

namespace Tsvrc.Config
{
    /// <summary>
    /// A single Global or Factory-prefab reference, plus which <see cref="TsGroup"/> it belongs to.
    /// </summary>
    [Serializable]
    public class TsGroupedEntry
    {
        /// <summary>The scene object, component, or prefab this entry registers.</summary>
        // Fully qualified: "using UnityEngine" plus "using System" (needed for [Serializable])
        // would make bare "Object" ambiguous between UnityEngine.Object and System.Object.
        public UnityEngine.Object Value;

        /// <summary>The owning group's <see cref="TsGroup.Id"/>, or 0 if ungrouped.</summary>
        public int GroupId;
    }
}
