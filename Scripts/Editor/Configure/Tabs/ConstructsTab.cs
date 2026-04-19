#if UNITY_EDITOR
namespace Tsvrc.Editor
{
    internal sealed class ConstructsTab : ObjectListTab
    {
        internal override string Description =>
            "Register TsvrcBehaviours that are always active in the scene, not pooled. TsConstruct() is called once on each at startup. Example: add your HudManager here and it is initialized automatically when the world loads.";
        protected override string PropertyName => "TsvrcBehaviourConstruct";
    }
}
#endif
