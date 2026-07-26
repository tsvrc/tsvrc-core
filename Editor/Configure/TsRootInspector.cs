#if UNITY_EDITOR
using Tsvrc.Core.Generated;
using UnityEditor;

namespace Tsvrc.Editor
{
    // TsRoot's base, UdonSharpBehaviour, has no literal [CustomEditor(typeof(UdonSharpBehaviour),
    // true)] (see UdonSharpBehaviourEditor.cs - it's commented out); instead UdonSharp patches
    // Unity's custom-editor table so every UdonSharpBehaviour resolves to
    // UdonSharpBehaviourOverrideEditor, which walks the target's base-type chain
    // (UdonSharpCustomEditorManager.InitInspectorMap) to find a user-registered CustomEditor -
    // conditioned on editorForChildClasses, which is what `true` below sets. It wraps this
    // editor's GUI with its own sync-mode/compile-status header and purple U# bar. Official
    // extension point, not an override.
    [CustomEditor(typeof(TsRoot), true)]
    internal class TsRootInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            TsEditorGUI.DrawManagedByConfigureBanner(
                "This is the Tsvrc-generated root behaviour. Its fields are auto-wired by the generator " +
                "- don't edit TsGenerated.cs directly. Configure singletons/pool/constructs/factories via " +
                "Tsvrc > Configure.");
            DrawDefaultInspector();
        }
    }
}
#endif
