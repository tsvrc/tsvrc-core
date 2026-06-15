#if UNITY_EDITOR
namespace Tsvrc.Editor
{
    internal sealed class PoolTab : ObjectListTab
    {
        internal override string Description =>
            "Register UdonSharpBehaviour prefabs to pool. " +
            "The system automatically instantiates all slots, initializes them, and wires every [WirePool] field across your behaviours at compile time. " +
            "No manual scene placement, no cross-behaviour drag-and-drop, and no broken references when you refactor.";

        protected override string PropertyName => "PooledObjects";
    }
}
#endif
