#if UNITY_EDITOR
namespace Tsvrc.Editor
{
    internal sealed class PoolTab : ObjectListTab
    {
        internal override string Label => "Pool";
        internal override string Description =>
            "Register TsvrcProcess behaviours whose slots are placed in the scene before play. VRChat assigns each slot a stable network ID, enabling network events. Use _ts.GetType() to borrow a slot and call TsRelease() when done. Only TsvrcProcess subclasses are valid entries. Example: add DataTransferer here, then call _ts.GetDataTransferer() at runtime.";
        protected override string PropertyName => "TsvrcProcessPool";
    }
}
#endif
