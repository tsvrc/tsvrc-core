using UnityEngine;

namespace Tsvrc.Utils
{
    /// <summary>
    /// Marks a serialized field as read-only in the Unity Inspector.
    /// Pair with ReadOnlyDrawer (Editor assembly) to render it grayed out.
    /// </summary>
    public class ReadOnlyAttribute : PropertyAttribute { }
}
