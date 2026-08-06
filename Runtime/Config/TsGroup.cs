using System;

namespace Tsvrc.Config
{
    /// <summary>
    /// One node in a Global/Factory group tree. Flat and parent-referencing rather than
    /// recursive, so it maps directly onto a TreeView's id/parent-id row model and avoids
    /// Unity's nested-type serialization depth limit.
    /// </summary>
    [Serializable]
    public class TsGroup
    {
        /// <summary>Unique within the owning config. Assigned once and never reused, even after deletion.</summary>
        public int Id;

        /// <summary>The parent group's <see cref="Id"/>, or 0 for a root-level group.</summary>
        public int ParentId;

        /// <summary>Display name, shown in the Configure window and used as a naming prefix for Factory groups.</summary>
        public string Name;
    }
}
