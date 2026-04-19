#if UNITY_EDITOR
namespace Tsvrc.Editor
{
    internal sealed class SingletonsTab : ObjectListTab
    {
        internal override string Label => "Singletons";
        internal override string Description =>
            "Register any scene object or component as a named field on _ts. After compiling, access it from any TsvrcBehaviour via _ts.FieldName. Example: drag your GameManager here, then use _ts.GameManager from any behaviour.";
        protected override string PropertyName => "Singletons";
    }
}
#endif
