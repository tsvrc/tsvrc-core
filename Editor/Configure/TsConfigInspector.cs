#if UNITY_EDITOR
using Tsvrc.Config;
using UnityEditor;

namespace Tsvrc.Editor
{
    // TsConfig is tagged EditorOnly, so a user can stumble onto it directly in the Hierarchy
    // without knowing Tsvrc > Configure exists - the banner below points them there. Falls back
    // to the default inspector since the array fields already have tooltips (see TsConfig.cs).
    [CustomEditor(typeof(TsConfig))]
    internal class TsConfigInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            TsEditorGUI.DrawManagedByConfigureBanner(
                "This object is auto-created and self-healed by Tsvrc. The fields below work here too, " +
                "but Tsvrc > Configure gives the same fields with per-tab guidance and empty-state hints.");
            DrawDefaultInspector();
        }
    }
}
#endif
